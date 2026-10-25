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

**Current limitations:** No per-client capabilities, no rate limits, and no write-back
authorization flow. Do not enable direct writes until a review/approval mechanism exists.

**Reporting vulnerabilities:** Do not include project files or tokens in public bug reports.
Contact maintainers privately via the repository owner's available security reporting channel.
