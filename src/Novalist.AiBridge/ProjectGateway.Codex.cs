using Novalist.Sdk.Services;

namespace Novalist.AiBridge;

internal sealed partial class ProjectGateway
{
    private const int CodexPreviewCharacters = 240;

    public async Task<object> GetCodexContextAsync(int chapterNumber, int sceneNumber, int offset, int limit)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");
        if (limit < 1 || limit > 10) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 10.");
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            var allowed = await AllowedContextAsync(Id(chapter), Id(scene));
            var page = allowed.Skip(offset).Take(limit).Select(entry => new
            {
                id = entry.Id, typeKey = entry.TypeKey, name = entry.Name, inclusion = entry.Inclusion,
                sections = entry.Sections.Select((section, index) =>
                {
                    var plain = HtmlToText(section.Content);
                    return new { index, title = section.Title, totalCharacters = plain.Length,
                        preview = plain[..Math.Min(plain.Length, CodexPreviewCharacters)] };
                }).ToArray()
            }).ToArray();
            return new
            {
                chapter = chapterNumber, scene = sceneNumber,
                totalEntries = allowed.Count, offset, returned = page.Length,
                nextOffset = offset + page.Length < allowed.Count ? offset + page.Length : (int?)null,
                entries = page,
                note = "Only scene-eligible Codex entries and AI-visible sections are returned, as filtered by Novalist. A missing entry may be intentionally withheld."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> GetCodexSectionAsync(int chapterNumber, int sceneNumber, string entryId, int sectionIndex, int start, int length, string? expectedHash = null)
    {
        ValidatePage(start, length);
        if (string.IsNullOrWhiteSpace(entryId)) throw new ArgumentException("An entry ID is required.", nameof(entryId));
        if (sectionIndex < 0) throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            var allowed = await AllowedContextAsync(Id(chapter), Id(scene));
            var entry = allowed.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
            if (entry is null) throw new ArgumentException("Entry is unavailable in the AI context for this scene.");
            if (sectionIndex >= entry.Sections.Count) throw new ArgumentOutOfRangeException(nameof(sectionIndex));
            var section = entry.Sections[sectionIndex];
            return new
            {
                chapter = chapterNumber, scene = sceneNumber,
                entryId = entry.Id, entryName = entry.Name, typeKey = entry.TypeKey,
                sectionIndex, sectionTitle = section.Title,
                page = PageText(HtmlToText(section.Content), start, length, expectedHash),
                note = "Only a Novalist-approved AI-visible section is readable. Offsets count UTF-16 code units."
            };
        }
        finally { _serial.Release(); }
    }

    private async Task<IReadOnlyList<AiContextEntryInfo>> AllowedContextAsync(string chapterId, string sceneId) =>
        await _host.EntityService.GetAiContextAsync(chapterId, sceneId).ConfigureAwait(false);
}
