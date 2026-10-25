# Contributing

Thanks for helping improve Novalist AI Bridge.

- For bugs, share Novalist version, operating system, exact command/tool called,
  and sanitized error details. Never attach manuscript data or authentication tokens.
- Keep the MCP interface independent from individual AI clients and individual books.
- Preserve the read-only MCP contract, SDK AI-visibility controls, and safe upgrade behavior.
- Run `python3 -m unittest discover -s tests -v` and
  `dotnet build src/Novalist.AiBridge/Novalist.AiBridge.csproj -c Release`.
- Do not add MCP tools that create or modify comments, tracked edits, scenes, chapters, or Codex entries. Return editorial observations to the AI client for author review instead.
- Describe new MCP methods and changes to the user-visible interface in the README
  and CHANGELOG.
