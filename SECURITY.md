# Security

Novalist AI Bridge exposes manuscript content to a local MCP client. **Treat the bearer token
as a password and do not paste it in issues, commits, logs, or screenshots.**

The bridge binds to `127.0.0.1:8765` and requires a bearer token for MCP requests.
The `/health` endpoint contains no project data. Token reveal and copy are handled
inside Novalist's extension view, not an unauthenticated web endpoint.

**Trust boundary:** Anyone with access to the token and the local MCP endpoint can read
manuscript content through the exposed tools. Localhost is not a replacement for
user account isolation; other software running under your account may be able to
reach localhost. Do not forward or tunnel port 8765 to other machines.

**Read-only design:** MCP tools expose saved manuscript and AI-permitted Codex data, not mutations. There are no comment-proposal, approval, tracked-edit, scene-write, or Codex-write tools. Token persistence and settings are local extension configuration, not manuscript write-back.\n\n**Current limitations:** No per-client capabilities or rate limits. The read-only bridge still exposes private manuscript content to authorized local clients.

**Reporting vulnerabilities:** Do not include project files or tokens in public bug reports.
Contact maintainers privately via the repository owner's available security reporting channel.
