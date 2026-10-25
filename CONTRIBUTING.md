# Contributing

Thanks for helping improve Novalist AI Bridge.

- For bugs, share Novalist version, operating system, exact command/tool called,
  and sanitized error details. Never attach manuscript data or authentication tokens.
- Keep the MCP interface independent from individual AI clients and individual books.
- Preserve read-only defaults, SDK AI-visibility controls, and safe upgrade behavior.
- Run `python3 -m unittest discover -s tests -v` and
  `dotnet build src/Novalist.AiBridge/Novalist.AiBridge.csproj -c Release`.
- For new write-back features, require preview/approval, revision checks, and
  reversible snapshots before applying changes.
- Describe new MCP methods and changes to the user-visible interface in the README
  and CHANGELOG.
