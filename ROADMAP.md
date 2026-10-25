# Roadmap

Novalist AI Bridge is intentionally **read-only**. MCP clients may read the author's saved manuscript and AI-eligible Codex context, but they cannot add comments, suggest edits, or modify scenes or lore through the bridge. Editorial feedback stays in the AI client's conversation or external reports; the author controls any changes in Novalist.

## v0.4 — Read-only retrieval (implemented)
- [x] Paginated scene excerpts with stable offsets and content hashes
- [x] Metadata and statistics without retrieving full scene prose
- [x] Scene-specific Codex access with Novalist's `GetAiContextAsync` visibility rules
- [x] Locally authenticated MCP endpoint and Novalist settings interface
- [ ] Configurable treatment of inactive and export-excluded scenes, preserving safe defaults

## Next — Better manuscript and lore context
- Improve cross-scene search and bounded retrieval for local AI models
- Add efficient character, location, and lore context discovery using the SDK's AI visibility policies
- Provide source references to the chapters, scenes, and Codex entries supporting an answer
- Make continuity-checking workflows easier without implementing any manuscript mutation

## Later — Larger projects and distribution
- Multi-book and draft-aware navigation and comparison where supported by the SDK
- Native extension settings and token rotation
- Verified Linux, Windows, and macOS builds
- CI coverage and reproducible release packages
- Extension gallery submission

**Out of scope:** MCP comments, edit proposals, approval queues, tracked edits, Codex writes, or direct manuscript writes.
