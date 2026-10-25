# Roadmap

## v0.4 — Retrieval (in progress)
- [x] Paginated/excerpt scene reads with stable offsets and content hashes
- [x] Metadata/stats endpoint that avoids returning full scene prose
- [x] Scene-specific AI-filtered Codex access via Novalist's SDK
- [x] Honor `GetAiContextAsync` visibility and inclusion policies for AI-bound content
- Configurable handling for excluded and inactive scenes, with safe defaults

## v0.5 — Editorial feedback (development)
- [x] Native comments and suggested edits, using Novalist review APIs
- [x] Writer-visible queue of proposed changes
- [x] Explicit in-Novalist approval, not approval by a model's tool call

## v0.6 — Controlled writes
- Optional Codex and scene creation/update methods
- Per-client capabilities and write-off by default
- Snapshots plus version/hash conflict detection before any prose overwrite
- Project identity checks and audit logs

## v1.0 — Distribution
- Native extension settings and token rotation
- Verified Linux, Windows and macOS builds
- CI build/integration coverage and reproducible release packages
- Extension gallery submission
