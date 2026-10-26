using Novalist.Sdk.Services;

namespace Novalist.AiBridge;

internal sealed partial class ProjectGateway
{
    private const int MaxProfileFieldCharacters = 300;
    private const int MaxProfileCollectionItems = 25;

    /// <summary>
    /// Read a real Codex entry, but only after Novalist's AI-context policy has
    /// authorized this exact entry in the requested scene. The SDK context API
    /// provides sanitized sections but omits ordinary typed profile fields and
    /// location/item/lore descriptions; fetch those separately *after* gating.
    ///
    /// Crucially, never serialize raw EntityContentInfo.Sections or
    /// CharacterDetailedInfo.Sections: they may contain AI-hidden material.
    /// Section text comes ONLY from the allowed AiContextEntryInfo.
    /// </summary>
    public async Task<object> GetCodexEntryAsync(int chapterNumber, int sceneNumber,
        string entryId, int start, int length, string? expectedHash = null)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            throw new ArgumentException("An entry ID is required.", nameof(entryId));
        ValidatePage(start, length);

        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            var chapterId = Id(chapter);
            var sceneId = Id(scene);
            var allowed = await AllowedContextAsync(chapterId, sceneId);
            var entry = allowed.FirstOrDefault(e =>
                string.Equals(e.Id, entryId, StringComparison.Ordinal));
            if (entry is null)
                throw new ArgumentException(
                    "Codex entry is not AI-eligible in this scene. The bridge will not read its unfiltered content.");

            // The entry itself has passed the host's AI inclusion policy.
            // Content is used ONLY for description/aliases, not raw sections.
            var details = await _host.EntityService.GetEntityContentAsync(entry.TypeKey, entry.Id)
                .ConfigureAwait(false);
            var description = details?.Description ?? "";
            var aliases = details?.Aliases.Take(MaxProfileCollectionItems)
                .Select(ShortField).ToArray() ?? [];

            object? characterProfile = null;
            if (string.Equals(entry.TypeKey, "character", StringComparison.OrdinalIgnoreCase))
            {
                var character = await _host.EntityService
                    .GetCharacterDetailedAsync(entry.Id, chapterId, sceneId)
                    .ConfigureAwait(false);
                if (character is not null)
                {
                    characterProfile = new
                    {
                        name = ShortField(character.Name),
                        surname = ShortField(character.Surname),
                        age = ShortField(character.Age),
                        gender = ShortField(character.Gender),
                        role = ShortField(character.Role),
                        group = ShortField(character.Group),
                        eyeColor = ShortField(character.EyeColor),
                        hairColor = ShortField(character.HairColor),
                        hairLength = ShortField(character.HairLength),
                        height = ShortField(character.Height),
                        build = ShortField(character.Build),
                        skinTone = ShortField(character.SkinTone),
                        distinguishingFeatures = ShortField(character.DistinguishingFeatures),
                        resolvedFromScope = ShortField(character.ResolvedFromScope),
                        relationships = character.Relationships
                            .Take(MaxProfileCollectionItems).Select(r => new
                            {
                                role = ShortField(r.Role),
                                targetName = ShortField(r.TargetName),
                                note = ShortField(r.Note)
                            }).ToArray(),
                        relationshipsTruncated = character.Relationships.Count > MaxProfileCollectionItems,
                        customProperties = character.CustomProperties.Take(MaxProfileCollectionItems)
                            .ToDictionary(p => p.Key, p => ShortField(p.Value)),
                        customPropertiesTruncated =
                            character.CustomProperties.Count > MaxProfileCollectionItems
                    };
                }
            }

            return new
            {
                chapter = chapterNumber, scene = sceneNumber,
                entryId = entry.Id, entryName = entry.Name,
                typeKey = entry.TypeKey, inclusion = entry.Inclusion,
                aliases,
                description = PageText(HtmlToText(description), start, length, expectedHash),
                characterProfile,
                availableSections = entry.Sections.Select((section, index) =>
                {
                    var plain = HtmlToText(section.Content);
                    return new
                    {
                        sectionIndex = index, sectionTitle = section.Title,
                        totalCharacters = plain.Length,
                        preview = plain[..Math.Min(plain.Length, CodexPreviewCharacters)]
                    };
                }).ToArray(),
                note = "AI eligibility is checked before reading extra Codex fields. The description is paginated; use get_codex_section for authorized sections. Raw/unfiltered sections are NEVER exposed. Profile values and collection sizes are bounded."
            };
        }
        finally { _serial.Release(); }
    }

    private static string ShortField(string? value)
    {
        value ??= "";
        return value.Length > MaxProfileFieldCharacters
            ? value[..MaxProfileFieldCharacters] + "…" : value;
    }
}
