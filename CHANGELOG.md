## v0.4.0 (development preview)

- Added paginated scene excerpts with stable content fingerprints and metadata/statistics endpoints.
- Added scene-scoped, AI-filtered Codex lookup and paginated sections using Novalist's `GetAiContextAsync`.
- Preserved existing read-only access and token management. Native runtime validation still pending.

# Changelog

## v0.3.3 (2026-10-24)

### Fixed
- Replaced speculative reflection-based chapter enumeration with the **verified Novalist SDK 15** `IExtensionProjectService.GetChaptersOrdered()` method. This directly addresses the `book_info`, `list_chapters`, `list_scenes` and `get_scene` failure seen in Unsloth.
- Scene listing now calls the verified `GetScenesForChapter(chapterGuid)` API; scene content now calls the verified `ReadSceneContentAsync(chapterGuid, sceneId)` API. All three are read-only.
- `book_info` now also returns the active draft's name using `GetDrafts()` and resolves the book title from `GetBooks()`.
- Detects a missing open project or active book with an actionable error instead of the misleading "SDK does not expose" error.

### Unchanged
- Native Novalist Settings panel Reveal, Hide and Copy Bearer Value controls introduced in v0.3.2.
- Extension ID, loopback MCP URL, token storage and HTTP authentication: existing Unsloth connection should work without changing the token.
- Filtering inactive and export-excluded scenes.

### Verification
- API method signatures were checked against the actual Novalist SDK repository's `IExtensionProjectService` interface, and the diagnostic output from the user's Novalist 3.6 installation.
- Python source tests and UI JavaScript simulation tests pass.
- Native .NET 10 compilation and Novalist runtime integration are **not** verified in this environment.

## v0.3.2

- See the previous release for removal of disappearing pairing codes and in-panel token controls.

## Repository preparation (no runtime changes)

- Added public README, contributor guide, security guidance, roadmap, `.gitignore`, and GitHub Actions build checks.
- No changes to the Novalist extension's functional C# code or version number.
