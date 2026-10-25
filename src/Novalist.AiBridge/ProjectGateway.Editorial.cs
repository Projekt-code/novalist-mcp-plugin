using Novalist.Sdk.Services;

namespace Novalist.AiBridge;

/// <summary>
/// In-memory, author-approved editorial queue. MCP callers can propose but have
/// no ability to approve or reject. Only a Novalist-hosted webview controller
/// invokes the apply methods.
/// </summary>
internal sealed partial class ProjectGateway
{
    private const int MaxEditorialProposals = 100;
    private const int MaxEditorialText = 4000;
    private const int MaxAnchorText = 600;
    private readonly Dictionary<string, EditorialProposal> _editorial = new();

    private sealed class EditorialProposal
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public required string Kind { get; init; }
        public required string ProjectRoot { get; init; }
        public required string BookId { get; init; }
        public required string DraftId { get; init; }
        public required string ChapterGuid { get; init; }
        public required string SceneId { get; init; }
        public required string ChapterTitle { get; init; }
        public required string SceneTitle { get; init; }
        public required string Anchor { get; init; }
        public required string Text { get; init; }
        public required string SavedTextHash { get; init; }
        public required string SavedHtmlHash { get; init; }
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    }

    public Task<object> ProposeCommentAsync(int chapter, int scene, string text, string anchor, string expectedHash) =>
        SubmitEditorialAsync("comment", chapter, scene, text, anchor, expectedHash);

    public Task<object> ProposeEditAsync(int chapter, int scene, string replacement, string anchor, string expectedHash) =>
        SubmitEditorialAsync("suggested_edit", chapter, scene, replacement, anchor, expectedHash);

    private async Task<object> SubmitEditorialAsync(string kind, int chapterNumber, int sceneNumber,
        string text, string anchor, string expectedHash)
    {
        if (kind is not ("comment" or "suggested_edit")) throw new ArgumentException("Unsupported proposal kind.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxEditorialText)
            throw new ArgumentException($"Text must contain 1 to {MaxEditorialText} characters.");
        if (anchor.Length > MaxAnchorText)
            throw new ArgumentException($"Anchor may not exceed {MaxAnchorText} characters.");
        if (kind == "suggested_edit" && string.IsNullOrWhiteSpace(anchor))
            throw new ArgumentException("Suggested edits require an exact original phrase in anchor.");
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new ArgumentException("expectedHash must be the scene contentHash returned by get_scene_stats.");

        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_editorial.Count >= MaxEditorialProposals)
                throw new InvalidOperationException("Review queue is full (100 proposals). Resolve or reject proposals first.");
            var project = _host.ProjectService;
            var root = project.ProjectRoot;
            var book = project.ActiveBookId;
            var draft = project.ActiveDraftId;
            if (!project.IsProjectLoaded || string.IsNullOrWhiteSpace(root)
                || string.IsNullOrWhiteSpace(book) || string.IsNullOrWhiteSpace(draft))
                throw new InvalidOperationException("A saved Novalist project, book, and draft must be active.");
            var (chapter, scene) = await ResolveSceneAsync(chapterNumber, sceneNumber).ConfigureAwait(false);
            var chapterId = Id(chapter);
            var sceneId = Id(scene);
            var html = await project.ReadSceneContentAsync(chapterId, sceneId).ConfigureAwait(false);
            var plain = HtmlToText(html);
            var textHash = ContentHash(plain);
            if (!string.Equals(textHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The saved scene differs from the supplied hash. Refresh get_scene_stats.");
            if (!string.IsNullOrEmpty(anchor) && !plain.Contains(anchor, StringComparison.Ordinal))
                throw new ArgumentException("The anchor does not appear in the saved scene.");
            // SDK's SuggestEditAsync searches literal HTML for the anchor text,
            // not the normalized plain text exposed by get_scene_excerpt.
            if (kind == "suggested_edit" && !OccursExactlyOnce(html, anchor))
                throw new ArgumentException("Suggested-edit anchor must occur exactly once in the saved HTML. Choose a shorter unformatted phrase.");

            var proposal = new EditorialProposal
            {
                Kind = kind, ProjectRoot = root, BookId = book, DraftId = draft,
                ChapterGuid = chapterId, SceneId = sceneId,
                ChapterTitle = Title(chapter), SceneTitle = Title(scene),
                Text = text, Anchor = anchor,
                SavedTextHash = textHash, SavedHtmlHash = ContentHash(html)
            };
            _editorial.Add(proposal.Id, proposal);
            return new
            {
                proposalId = proposal.Id, state = "pending_review", type = kind,
                chapter = chapterNumber, scene = sceneNumber,
                note = "Queued ONLY. No file, comment, or manuscript content was changed. The writer must approve this proposal inside Novalist."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> PendingEditorialAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            return new
            {
                proposals = _editorial.Values.OrderBy(p => p.CreatedAtUtc).Select(p => new
                {
                    id = p.Id, kind = p.Kind,
                    chapterTitle = p.ChapterTitle, sceneTitle = p.SceneTitle,
                    anchor = p.Anchor, text = p.Text, createdAtUtc = p.CreatedAtUtc
                }).ToArray(),
                note = "Proposal list is memory-only; restarting Novalist clears unreviewed proposals."
            };
        }
        finally { _serial.Release(); }
    }

    public async Task<object> RejectEditorialAsync(string id)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_editorial.Remove(id))
                throw new ArgumentException("Proposal not found or already resolved.");
            return new { id, state = "rejected" };
        }
        finally { _serial.Release(); }
    }

    /// <summary>Called only by ReviewWebViewController; never exposed as an MCP method.</summary>
    public async Task<object> ApproveEditorialAsync(string id)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_editorial.TryGetValue(id, out var p))
                throw new ArgumentException("Proposal not found or already resolved.");
            var project = _host.ProjectService;
            if (!project.IsProjectLoaded
                || !string.Equals(project.ProjectRoot, p.ProjectRoot, StringComparison.Ordinal)
                || !string.Equals(project.ActiveBookId, p.BookId, StringComparison.Ordinal)
                || !string.Equals(project.ActiveDraftId, p.DraftId, StringComparison.Ordinal))
                throw new InvalidOperationException("Project, book, or draft changed. Return to the original manuscript or reject the proposal.");
            var scene = project.GetScenesForChapter(p.ChapterGuid).FirstOrDefault(s => s.Id == p.SceneId);
            if (scene is null || scene.Inactive || scene.ExcludeFromExport)
                throw new InvalidOperationException("The target scene is missing or no longer accessible.");
            if (await project.IsSceneBusyAsync(p.ChapterGuid, p.SceneId).ConfigureAwait(false))
                throw new InvalidOperationException("The scene has unsaved changes. Save it in Novalist before approving.");
            var html = await project.ReadSceneContentAsync(p.ChapterGuid, p.SceneId).ConfigureAwait(false);
            if (!string.Equals(ContentHash(html), p.SavedHtmlHash, StringComparison.Ordinal)
                || !string.Equals(ContentHash(HtmlToText(html)), p.SavedTextHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The saved scene changed since the proposal was created. Reject it and request a fresh proposal.");
            if (p.Kind == "suggested_edit" && !OccursExactlyOnce(html, p.Anchor))
                throw new InvalidOperationException("The suggested-edit anchor is no longer unique in the saved scene.");

            if (p.Kind == "comment")
            {
                var createdId = await _host.ReviewService.AddCommentAsync(
                    p.ChapterGuid, p.SceneId, p.Anchor, p.Text, "Novalist AI Bridge").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(createdId))
                    throw new InvalidOperationException("Novalist did not create the comment.");
            }
            else
            {
                // SuggestEditAsync inserts a tracked suggestion into scene HTML.
                // Capture a recovery snapshot before allowing that SDK operation.
                var snapshotId = await _host.ArchiveService.TakeSnapshotAsync(
                    p.ChapterGuid, p.SceneId, "Before AI Bridge suggestion " + p.Id[..8]).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(snapshotId))
                    throw new InvalidOperationException("Could not create recovery snapshot; no edit applied.");
                var applied = await _host.ReviewService.SuggestEditAsync(
                    p.ChapterGuid, p.SceneId, p.Anchor, p.Text, "Novalist AI Bridge").ConfigureAwait(false);
                if (!applied)
                    throw new InvalidOperationException("Novalist declined the suggested edit.");
            }
            _editorial.Remove(id);
            return new { id, state = "approved", appliedAs = p.Kind == "comment" ? "scene_comment" : "tracked_suggestion" };
        }
        finally { _serial.Release(); }
    }

    private static bool OccursExactlyOnce(string text, string anchor)
    {
        var first = text.IndexOf(anchor, StringComparison.Ordinal);
        return first >= 0 && text.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal) < 0;
    }
}
