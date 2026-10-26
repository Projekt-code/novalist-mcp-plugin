using Novalist.Sdk.Services;

namespace Novalist.AiBridge;

/// <summary>
/// Bounded, source-addressable evidence retrieval for continuity reviews.
/// Both tools are read-only and respect existing visibility boundaries:
/// manuscript search skips excluded/inactive scenes, and Codex search uses
/// only GetAiContextAsync (never the raw entity lists).
/// </summary>
internal sealed partial class ProjectGateway
{
    private const int MaxEvidenceQueryLength = 120;
    private const int MaxEvidencePageSize = 20;
    private const int EvidenceContextCharacters = 110;

    public async Task<object> FindMentionsAsync(string query, int offset, int limit,
        int chapterStart, int chapterEnd, bool wholeWord)
    {
        query = ValidateEvidenceQuery(query);
        ValidateEvidencePage(offset, limit);

        if (chapterStart < 1)
            throw new ArgumentOutOfRangeException(nameof(chapterStart), "chapterStart must be 1 or greater.");
        if (chapterEnd < 0 || (chapterEnd != 0 && chapterEnd < chapterStart))
            throw new ArgumentOutOfRangeException(nameof(chapterEnd),
                "chapterEnd must be zero (all remaining chapters) or at least chapterStart.");

        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var chapters = await ChaptersCoreAsync();
            if (chapterStart > chapters.Count)
                throw new ArgumentOutOfRangeException(nameof(chapterStart), "chapterStart exceeds the chapter count.");

            var lastChapter = chapterEnd == 0 ? chapters.Count : Math.Min(chapterEnd, chapters.Count);
            var matches = new List<object>();
            var totalMatches = 0;
            var scannedScenes = 0;
            for (var c = chapterStart - 1; c < lastChapter; c++)
            {
                var chapter = chapters[c];
                var chapterId = Id(chapter);
                var scenes = await ScenesCoreAsync(chapterId);
                for (var s = 0; s < scenes.Count; s++)
                {
                    var scene = scenes[s];
                    scannedScenes++;
                    var sceneId = Id(scene);
                    var text = await ReadTextCoreAsync(chapterId, sceneId);
                    string? sceneHash = null;
                    foreach (var position in FindLiteralOccurrences(text, query, wholeWord))
                    {
                        if (totalMatches >= offset && matches.Count < limit)
                        {
                            sceneHash ??= ContentHash(text);
                            matches.Add(new
                            {
                                chapter = c + 1, chapterTitle = Title(chapter), chapterId,
                                scene = s + 1, sceneTitle = Title(scene), sceneId,
                                characterOffset = position,
                                matchedText = text.Substring(position, query.Length),
                                excerpt = ContextSnippet(text, position, query.Length),
                                contentHash = sceneHash
                            });
                        }
                        totalMatches++;
                    }
                }
            }
            return new
            {
                query, wholeWord, chapterStart, chapterEnd = lastChapter,
                scannedScenes, totalMatches, offset, returned = matches.Count,
                nextOffset = (long)offset + matches.Count < totalMatches ? offset + matches.Count : (int?)null,
                matches,
                note = "Exact literal text matches (case-insensitive) from saved, visible manuscript scenes only. Offsets refer to UTF-16 code units in get_scene_excerpt; contentHash can verify follow-up reads. These are evidence hits, not automatically detected continuity errors."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> SearchCodexContextAsync(int chapterNumber, int sceneNumber,
        string query, int offset, int limit)
    {
        query = ValidateEvidenceQuery(query);
        ValidateEvidencePage(offset, limit);

        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            // This SDK API enforces AI inclusion settings and removes withheld
            // sections. Never use Load*Async/GetEntityContentAsync as a shortcut.
            var allowed = await AllowedContextAsync(Id(chapter), Id(scene));
            var results = new List<object>();
            var totalMatches = 0;
            foreach (var entry in allowed)
            {
                var nameMatch = entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var sectionHits = new List<object>();
                for (var index = 0; index < entry.Sections.Count; index++)
                {
                    var section = entry.Sections[index];
                    var text = HtmlToText(section.Content);
                    var pos = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    var titleMatch = section.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (pos < 0 && !titleMatch) continue;
                    // Bound the section previews returned for a single entry,
                    // without affecting whether that entry matches the search.
                    if (sectionHits.Count < 3)
                        sectionHits.Add(new
                        {
                            sectionIndex = index, sectionTitle = section.Title,
                            matchedTitle = titleMatch,
                            characterOffset = pos >= 0 ? pos : (int?)null,
                            excerpt = pos >= 0 ? ContextSnippet(text, pos, query.Length) : "",
                            totalCharacters = text.Length
                        });
                }
                if (!nameMatch && sectionHits.Count == 0) continue;
                if (totalMatches >= offset && results.Count < limit)
                {
                    // Safe to reveal only the titles/indices of sections already
                    // accepted by Novalist's AI policy.
                    results.Add(new
                    {
                        entryId = entry.Id, name = entry.Name, typeKey = entry.TypeKey,
                        inclusion = entry.Inclusion, matchedName = nameMatch,
                        matchedSections = sectionHits,
                        availableSections = entry.Sections.Select((section, index) =>
                            new { sectionIndex = index, sectionTitle = section.Title }).ToArray()
                    });
                }
                totalMatches++;
            }
            return new
            {
                chapter = chapterNumber, scene = sceneNumber, query,
                eligibleEntries = allowed.Count, totalMatches, offset, returned = results.Count,
                nextOffset = (long)offset + results.Count < totalMatches ? offset + results.Count : (int?)null,
                entries = results,
                note = "Search is restricted to Novalist's scene-specific AI-eligible Codex entries and visible sections. Empty results do not mean a fact does not exist in the writer's Codex. Use get_codex_section for full approved text."
            };
        }
        finally { _serial.Release(); }
    }

    private static string ValidateEvidenceQuery(string query)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2 || query.Length > MaxEvidenceQueryLength)
            throw new ArgumentException($"Query must have 2 to {MaxEvidenceQueryLength} characters.");
        return query;
    }

    private static void ValidateEvidencePage(int offset, int limit)
    {
        if (offset < 0 || offset > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be between 0 and 1,000,000.");
        if (limit < 1 || limit > MaxEvidencePageSize)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {MaxEvidencePageSize}.");
    }

    private static IEnumerable<int> FindLiteralOccurrences(string text, string query, bool wholeWord)
    {
        for (var start = 0; start < text.Length;)
        {
            var position = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (position < 0) yield break;
            var end = position + query.Length;
            if (!wholeWord ||
                ((position == 0 || !WordCharacter(text[position - 1])) &&
                 (end == text.Length || !WordCharacter(text[end]))))
                yield return position;
            start = end; // No overlapping matches; always advances.
        }
    }

    private static bool WordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static string ContextSnippet(string text, int position, int length)
    {
        var from = Math.Max(0, position - EvidenceContextCharacters);
        var to = Math.Min(text.Length, position + length + EvidenceContextCharacters);
        var snippet = text[from..to].Replace('\r', ' ').Replace('\n', ' ');
        return (from > 0 ? "…" : "") + snippet + (to < text.Length ? "…" : "");
    }
}
