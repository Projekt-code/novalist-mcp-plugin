using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.AiBridge;

internal sealed partial class ProjectGateway
{
    private const int MaxPageCharacters = 4000;

    public async Task<object> GetSceneMetadataAsync(int chapterNumber, int sceneNumber)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            // Metadata deliberately does not load or return manuscript prose.
            return new
            {
                chapter = chapterNumber, chapterId = Id(chapter), chapterTitle = Title(chapter),
                scene = sceneNumber, sceneId = Id(scene), sceneTitle = Title(scene),
                wordCount = HostReflection.Property(scene, "WordCount"),
                note = "Metadata only. Use get_scene_excerpt for saved prose."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> GetSceneExcerptAsync(int chapterNumber, int sceneNumber, int start, int length, string? expectedHash = null)
    {
        ValidatePage(start, length);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            var text = await ReadTextCoreAsync(Id(chapter), Id(scene));
            return new
            {
                chapter = chapterNumber, scene = sceneNumber,
                chapterId = Id(chapter), sceneId = Id(scene),
                sceneTitle = Title(scene),
                page = PageText(text, start, length, expectedHash),
                note = "Offsets count UTF-16 code units in the saved, HTML-stripped text. Unsaved edits may not be included."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> GetSceneStatsAsync(int chapterNumber, int sceneNumber)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber);
            var text = await ReadTextCoreAsync(Id(chapter), Id(scene));
            // Do not return full prose merely to answer length or beginning/end questions.
            var wordCount = Regex.Matches(text, @"\S+").Count;
            var prefix = text[..Math.Min(text.Length, 240)];
            var suffix = text[Math.Max(0, text.Length - 240)..];
            return new
            {
                chapter = chapterNumber, scene = sceneNumber,
                sceneId = Id(scene), sceneTitle = Title(scene),
                totalCharacters = text.Length, wordCount, contentHash = ContentHash(text),
                firstCharacters = prefix, lastCharacters = suffix,
                note = "Character counts and offsets are UTF-16 code units. First/last samples are excerpts, not necessarily complete sentences."
            };
        }
        finally { _serial.Release(); }
    }

    private async Task<(object chapter, object scene)> ResolveSceneAsync(int chapterNumber, int sceneNumber)
    {
        var (chapter, scenes) = await SceneIndexAsync(chapterNumber);
        if (sceneNumber < 1 || sceneNumber > scenes.Count)
            throw new ArgumentOutOfRangeException(nameof(sceneNumber), $"Chapter {chapterNumber} has {scenes.Count} available scenes.");
        return (chapter, scenes[sceneNumber - 1]);
    }

    private static void ValidatePage(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Start must be non-negative.");
        if (length < 1 || length > MaxPageCharacters)
            throw new ArgumentOutOfRangeException(nameof(length), $"Length must be between 1 and {MaxPageCharacters} characters.");
    }

    private static string ContentHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static object PageText(string text, int start, int length, string? expectedHash)
    {
        var hash = ContentHash(text);
        if (!string.IsNullOrWhiteSpace(expectedHash) && !string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Content changed since the previous page. Restart at offset zero.");
        if (start > text.Length) throw new ArgumentOutOfRangeException(nameof(start), "Start exceeds the text length.");
        // Never split a UTF-16 surrogate pair. The next page uses the returned nextStart value.
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start])) start--;
        var end = Math.Min(text.Length, checked(start + length));
        if (end < text.Length && end > start && char.IsLowSurrogate(text[end])) end--;
        // For length=1 on a surrogate pair, consume both code units to guarantee progress.
        if (end == start && start + 1 < text.Length && char.IsHighSurrogate(text[start])) end = start + 2;
        return new
        {
            text = text[start..end], start, nextStart = end,
            totalCharacters = text.Length, hasMore = end < text.Length,
            contentHash = hash
        };
    }
}
