# Novalist AI Bridge

**An experimental, local MCP bridge for Novalist 3.6+**, allowing compatible AI clients—including Unsloth Studio—to discover and read the active Novalist manuscript.

> **Status:** v0.5.0 development preview. Chapter and scene retrieval have been tested with a Novalist 3.6 Linux installation. Large scene responses may overwhelm an AI client's context window. Review the [security notes](SECURITY.md) before connecting other clients.

Novalist AI Bridge is a community extension, not part of Novalist's official distribution.

## Current features

- Local Streamable HTTP MCP endpoint: `http://127.0.0.1:8765/mcp/`
- Bearer-token authentication, generated and stored locally
- Discover active book, draft, chapters and scenes
- Read and search the active manuscript
- SDK diagnostics for troubleshooting
- In-Novalist token controls: Reveal, Hide, and Copy Bearer value
- Read-only scene metadata, statistics and paginated excerpts
- Scene-scoped Codex entries and sections filtered through Novalist’s `GetAiContextAsync`
- Bounded, cited manuscript mention searches and scene-scoped Codex searches
- AI-authorized Codex entry details: descriptions, structured character traits, relationships, and available section previews
- Deliberately **read-only**: no comment proposals, editorial approval queue, suggested edits, scene writes, or Codex writes.

**Data scope:** The bridge currently omits inactive and export-excluded scenes. Scene reads return saved text (unsaved editor changes may not yet be visible). Large `get_scene` results may be truncated by the bridge or the AI client.

## Requirements

- Novalist **3.6+** with extension support
- **.NET 10 SDK** to build
- A local MCP client that supports Streamable HTTP and custom request headers
- Python 3 (only for tests and the standalone MCP smoke-test script)

## Build on Linux

```bash
./build.sh
```

Then in Novalist use **Go → Extensions → Installed → Install from Folder** and select:

`dist/com.whscott.novalist-ai-bridge`

Open a project and navigate to **World → From extensions → AI Bridge Settings** to reveal or copy the authorization header. The existing token is retained when upgrading with the same extension ID and settings directory.

## Connect an MCP client

| Field | Value |
|---|---|
| Transport | Streamable HTTP |
| URL | `http://127.0.0.1:8765/mcp/` |
| Header name | `Authorization` |
| Header value | `Bearer <token copied from Novalist>` |

The local health endpoint can be checked without a token:

```bash
curl http://127.0.0.1:8765/health
```

Example test prompt for a tool-capable client:

> Use the Novalist MCP tools to find the active book, list its chapters, then list the scenes in its first chapter. Report the returned titles and identifiers. Do not invent missing details.

Prefer `get_scene_stats` and paginated `get_scene_excerpt` over multiple full-scene reads, especially with smaller local models.

## MCP tools

| Tool | Purpose |
|---|---|
| `book_info` | Active book and draft |
| `list_chapters` | Ordered chapter list |
| `list_scenes` | Active scenes in one chapter |
| `get_scene` | Legacy full saved scene text |
| `get_scene_metadata` | Metadata without prose |
| `get_scene_stats` | Character/word counts, hash and beginning/end samples |
| `get_scene_excerpt` | Paginated text (max 4000 UTF-16 code units), with nextStart and contentHash |
| `get_codex_context` | Paged AI-eligible Codex entries for a scene |
| `get_codex_section` | Paged text from an allowed Codex section |
| `search_scenes` | Search scenes for passages |
| `find_mentions` | Paginated exact mentions across visible scenes with offsets, hashes, and evidence excerpts |
| `search_codex_context` | Find eligible Codex entries and section matches within one scene's AI context |
| `get_codex_entry` | Read an AI-authorized entry's paginated description, character traits, relationships, and approved section previews |
| `sdk_diagnostics` | SDK method names for troubleshooting |

## Development

Run the source-level tests:

```bash
python3 -m unittest discover -s tests -v
```

The test suite is **not a substitute for a native build or in-app integration test**.

For source compilation:

```bash
dotnet build src/Novalist.AiBridge/Novalist.AiBridge.csproj -c Release
```

The current extension code targets `Novalist.Sdk` v15.0.0. See the [official Novalist extension guide](https://github.com/Drommedhar/novalist-official/blob/main/docs/extension-guide.md).

## Roadmap and contributing

Read [ROADMAP.md](ROADMAP.md) for retrieval, context search, and continuity-checking priorities. Manuscript changes remain entirely under the author's control in Novalist. Contributions and reproducible bug reports are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT © 2026 W.H. Scott. See [LICENSE](LICENSE).
