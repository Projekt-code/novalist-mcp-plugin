using System.Net;
using System.Text.RegularExpressions;
using Novalist.Sdk.Services;

namespace Novalist.AiBridge;

/// <summary>Read-only access to the active book/draft. Inactive and export-excluded scenes are hidden.</summary>
internal sealed partial class ProjectGateway
{
    private readonly IHostServices _host;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private const int MaxSceneCharacters = 50000;
    private const int MaxSearchQueryLength = 120;

    public ProjectGateway(IHostServices host) => _host = host;

    public async Task<object> BookInfoAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var projectService = _host.ProjectService;
            var storyService = _host.StoryService;
            object? detail = null;
            if (storyService is not null)
            {
                try { detail = await HostReflection.ReadAsync(storyService, ["GetBookDetail", "GetBookDetailAsync"]); }
                catch (NotSupportedException) { /* Optional SDK feature. */ }
            }
            var chapters = await ChaptersCoreAsync();
            var bookId = projectService.ActiveBookId;
            var draftId = projectService.ActiveDraftId;
            var book = projectService.GetBooks().FirstOrDefault(item => item.Id == bookId);
            var draft = projectService.GetDrafts().FirstOrDefault(item => item.Id == draftId);
            return new
            {
                bookId,
                draftId,
                bookTitle = book?.Name ?? HostReflection.Str(detail, "Name", "Title"),
                draftName = draft?.Name ?? "",
                genre = HostReflection.Str(HostReflection.Property(detail, "Pitch"), "Genre"),
                chapterCount = chapters.Count,
                note = "Information is from Novalist's active book/draft; no project files were accessed directly."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> ListChaptersAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var chapters = await ChaptersCoreAsync();
            return new
            {
                chapters = chapters.Select((chapter, index) => new
                {
                    chapter = index + 1,
                    id = Id(chapter),
                    title = Title(chapter),
                    sceneCount = HostReflection.Property(chapter, "SceneCount"),
                }).ToArray()
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> ListScenesAsync(int chapterNumber)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scenes) = await SceneIndexAsync(chapterNumber);
            return new
            {
                chapter = chapterNumber,
                chapterId = Id(chapter),
                chapterTitle = Title(chapter),
                scenes = scenes.Select((scene, i) => new
                {
                    scene = i + 1,
                    id = Id(scene),
                    title = Title(scene),
                    stage = HostReflection.Str(scene, "Stage", "Status"),
                    wordCount = HostReflection.Property(scene, "WordCount"),
                }).ToArray()
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> GetSceneAsync(int chapterNumber, int sceneNumber)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try { return await SceneCoreAsync(chapterNumber, sceneNumber); }
        finally { _serial.Release(); }
    }

    public async Task<object> SearchScenesAsync(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2 || query.Length > MaxSearchQueryLength)
            throw new ArgumentException($"A query of 2–{MaxSearchQueryLength} characters is required.");
        query = query.Trim();
        limit = Math.Clamp(limit, 1, 25);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var chapters = await ChaptersCoreAsync();
            var results = new List<object>();
            var matchedScenes = 0;
            var scannedScenes = 0;
            for (var c = 0; c < chapters.Count; c++)
            {
                var chapter = chapters[c];
                var scenes = await ScenesCoreAsync(Id(chapter));
                for (var s = 0; s < scenes.Count; s++)
                {
                    scannedScenes++;
                    var scene = scenes[s];
                    var text = await ReadTextCoreAsync(Id(chapter), Id(scene));
                    var position = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (position < 0) continue;
                    matchedScenes++;
                    if (results.Count >= limit) continue;
                    var first = Math.Max(0, position - 110);
                    var last = Math.Min(text.Length, position + query.Length + 160);
                    var excerpt = text[first..last].Replace('\n', ' ').Trim();
                    results.Add(new
                    {
                        chapter = c + 1,
                        chapterTitle = Title(chapter),
                        scene = s + 1,
                        sceneId = Id(scene),
                        sceneTitle = Title(scene),
                        excerpt,
                        matchCharacterIndex = position,
                    });
                }
            }
            return new { query, scannedScenes, matchedScenes, returned = results.Count, results };
        }
        finally { _serial.Release(); }
    }

    private async Task<object> SceneCoreAsync(int chapterNumber, int sceneNumber)
    {
        var (chapter, scenes) = await SceneIndexAsync(chapterNumber);
        if (sceneNumber < 1 || sceneNumber > scenes.Count)
            throw new ArgumentOutOfRangeException(nameof(sceneNumber), $"Chapter {chapterNumber} has {scenes.Count} active, visible scenes.");
        var scene = scenes[sceneNumber - 1];
        var text = await ReadTextCoreAsync(Id(chapter), Id(scene));
        return new
        {
            chapter = chapterNumber,
            chapterId = Id(chapter),
            chapterTitle = Title(chapter),
            scene = sceneNumber,
            sceneId = Id(scene),
            sceneTitle = Title(scene),
            text = text.Length > MaxSceneCharacters ? text[..MaxSceneCharacters] : text,
            truncated = text.Length > MaxSceneCharacters,
            totalCharacters = text.Length,
            note = "Original text is read-only. Unsaved editor edits may not be included until Novalist saves the scene."
        };
    }

    private async Task<(object chapter, List<object> scenes)> SceneIndexAsync(int chapterNumber)
    {
        var chapters = await ChaptersCoreAsync();
        if (chapterNumber < 1 || chapterNumber > chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(chapterNumber), $"Active draft has {chapters.Count} chapters.");
        var chapter = chapters[chapterNumber - 1];
        return (chapter, await ScenesCoreAsync(Id(chapter)));
    }

    /// <summary>
    /// Read the active manuscript chapter list using Novalist SDK 15's concrete
    /// contract (IExtensionProjectService.GetChaptersOrdered). This replaces the
    /// guessed method-name probing in v0.3.2.
    /// </summary>
    private Task<List<object>> ChaptersCoreAsync()
    {
        var project = _host.ProjectService;
        if (!project.IsProjectLoaded)
            throw new InvalidOperationException("Open a Novalist project before querying the bridge.");
        if (string.IsNullOrWhiteSpace(project.ActiveBookId))
            throw new InvalidOperationException("Select a book in Novalist before querying chapters.");

        var chapters = project.GetChaptersOrdered()
            .Cast<object>()
            .ToList();
        return Task.FromResult(SortChapters(chapters));
    }

    private static List<object> SortChapters(List<object> chapters)
    {
        if (chapters.Count > 0 && chapters.All(c => HostReflection.Property(c, "Order", "SortOrder", "Position", "Index") is not null))
            chapters.Sort(HostReflection.CompareOrder);
        return chapters;
    }

    public object SdkDiagnostics()
    {
        return new
        {
            bridge = "0.4.0",
            project = HostReflection.SafeApiDescription(_host.ProjectService),
            archive = HostReflection.SafeApiDescription(HostReflection.Property(_host, "ArchiveService")),
            story = HostReflection.SafeApiDescription(_host.StoryService),
            activeBookIdPresent = !string.IsNullOrWhiteSpace(HostReflection.Str(_host.ProjectService, "ActiveBookId")),
            activeDraftIdPresent = !string.IsNullOrWhiteSpace(HostReflection.Str(_host.ProjectService, "ActiveDraftId")),
            hint = "API signatures and boolean service state only. No manuscript, project IDs, paths, token, or content."
        };
    }

    private Task<List<object>> ScenesCoreAsync(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            throw new InvalidOperationException("Novalist returned a chapter without a GUID.");

        var scenes = _host.ProjectService.GetScenesForChapter(chapterId)
            .Where(s => !s.Inactive && !s.ExcludeFromExport)
            .Cast<object>()
            .ToList();
        return Task.FromResult(scenes);
    }

    private async Task<string> ReadTextCoreAsync(string chapterId, string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new InvalidOperationException("Novalist returned a scene without an ID.");
        // Novalist SDK 15: Task<string> ReadSceneContentAsync(string chapterGuid, string sceneId)
        var html = await _host.ProjectService.ReadSceneContentAsync(chapterId, sceneId)
            .ConfigureAwait(false);
        return HtmlToText(html);
    }

    private static string Id(object record) => HostReflection.Str(record, "Id", "Guid", "ChapterGuid", "SceneId");
    private static string Title(object record) => HostReflection.Str(record, "Title", "Name", "Label");
    private static bool Visible(object scene) => !HostReflection.Bool(scene, "Inactive", "Archived")
        && !HostReflection.Bool(scene, "ExcludeFromExport");

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var withoutComments = Regex.Replace(html, @"<!--[\s\S]*?-->", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        var withoutScripts = Regex.Replace(withoutComments, @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var paragraphs = Regex.Replace(withoutScripts, @"</?(?:p|div|section|article|blockquote|li|h[1-6])\b[^>]*>|<br\s*/?>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var plain = Regex.Replace(paragraphs, @"<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        plain = WebUtility.HtmlDecode(plain).Replace('\u00A0', ' ');
        plain = Regex.Replace(plain, @"[ \t]+\n", "\n", RegexOptions.None, TimeSpan.FromSeconds(1));
        return Regex.Replace(plain, @"\n{3,}", "\n\n", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
    }
}
