# Novalist AI Bridge

**An experimental, local MCP bridge for Novalist 3.6+**, allowing compatible AI clients—including Unsloth Studio—to discover and read the active Novalist manuscript.

> **Status:** v0.3.3 pre-release. Chapter and scene retrieval have been tested with a Novalist 3.6 Linux installation. Large scene responses may overwhelm an AI client's context window. Review the [security notes](SECURITY.md) before connecting other clients.

Novalist AI Bridge is a community extension, not part of Novalist's official distribution.

## Current features

- Local Streamable HTTP MCP endpoint: `http://127.0.0.1:8765/mcp/`
- Bearer-token authentication, generated and stored locally
- Discover active book, draft, chapters and scenes
- Read and search the active manuscript
- SDK diagnostics for troubleshooting
- In-Novalist token controls: Reveal, Hide, and Copy Bearer value
- No manuscript writes or Codex access yet

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

Avoid asking for multiple full scenes in a single tool cycle until paginated retrieval is implemented.

## MCP tools

| Tool | Purpose |
|---|---|
| `book_info` | Active book and draft |
| `list_chapters` | Ordered chapter list |
| `list_scenes` | Active scenes in one chapter |
| `get_scene` | Saved scene text |
| `search_scenes` | Search scenes for passages |
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

Read [ROADMAP.md](ROADMAP.md) for paginated scenes, Codex access, and safe editorial write-back. Contributions and reproducible bug reports are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT © 2026 W.H. Scott. See [LICENSE](LICENSE).
