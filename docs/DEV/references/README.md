# DEV / references

Vendored, third-party reference material for backend development — dropped-in knowledge files
(not skills, not code) that `@backend-dev`, reviewers, and humans can read for concrete .NET
guidance specific to decisions this repo faces.

| File | What it is | Provenance |
|---|---|---|
| [dotnet-common-antipatterns.md](dotnet-common-antipatterns.md) | BAD/GOOD C# cheat-sheet (async void, sync-over-async, `new HttpClient()`, `DateTime.Now`, catch-`Exception`, service locator, tracking-for-reads, scoped-in-singleton, missing `CancellationToken`, …). Backs the `antipattern-advisor` PreToolUse hook. | [`codewithmukesh/dotnet-claude-kit`](https://github.com/codewithmukesh/dotnet-claude-kit) · MIT © 2025 Mukesh Murugan |
| [mediatr-to-mediator-migration.md](mediatr-to-mediator-migration.md) | Decision-support for the MediatR v13+ commercial-licensing change + a mechanical migration path to the MIT source-generated `Mediator`. **Not a scheduled migration** — a watch-item to evaluate against our pinned version. | [`codewithmukesh/dotnet-claude-kit`](https://github.com/codewithmukesh/dotnet-claude-kit) · MIT © 2025 Mukesh Murugan |

**License:** both files are MIT (© 2025 Mukesh Murugan) and lightly retargeted to this repo; the MIT
copyright/permission notice is retained by attribution above. Deeper, auto-loaded C#/EF standards
come from the vendored [`Aaronontheweb/dotnet-skills`](https://github.com/Aaronontheweb/dotnet-skills)
plugin (`csharp-coding-standards`, `efcore-patterns`, …) — these two files are the discrete,
decision-specific extras that plugin doesn't cover.
