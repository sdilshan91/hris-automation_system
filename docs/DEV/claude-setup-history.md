# Claude Code setup — history and rationale

> Moved out of [CLAUDE.md](../../CLAUDE.md) on 2026-09-01. This is the **why** behind the
> current `.claude/` setup: which plugins were installed or removed and on what reasoning,
> which third-party skills were vendored rather than installed, and the traps this repo has
> already fallen into. It is reference material, not standing instruction — CLAUDE.md loads
> in full on every agent run, and this content is changelog rather than protocol, so keeping
> it here removes ~2,300 tokens from every session without losing a word of it.
>
> Read this before changing plugins, marketplaces, or the vendored skill set.
> `/retro`'s setup-drift pass is the recurring check that these claims are still true.

> **Locally-vendored discipline skills.** `/fault-diagnosis` and `/error-recovery` live in
> [`.claude/skills/`](.claude/skills/) (adapted from third-party MIT skill definitions, retargeted to
> this stack — Serilog/`RequestId`, EF/Postgres, xUnit/Karma/Playwright). They are guidance protocols,
> not pipeline drivers; invoke them explicitly or let them fire on bug/stuck-loop triggers. They defer to
> the `test-integrity-guard` hook and the `/implement-all` remediation loop rather than competing with them.

> **.NET reference skills — VENDORED, not a plugin (changed 2026-08-23).** 16 on-stack skills from the
> MIT-licensed [`dotnet-skills`](https://github.com/Aaronontheweb/dotnet-skills) v1.5.0 now live directly in
> [.claude/skills/](.claude/skills/): **`efcore-patterns`** (NoTracking-by-default, query splitting, CLI-only
> migrations — reinforces our "never hand-write migrations" rule), **`testcontainers`** (our integration-test
> approach), `database-performance`, `csharp-api-design`/`-coding-standards`, `csharp-nullable-reference-types`
> (every project sets `<Nullable>enable</Nullable>` and the build emits CS8602/CS8604), the
> `microsoft-extensions-*` DI/config pair, `project-structure`, `package-management`, `serialization`,
> `snapshot-testing` (Verify), `opentelementry-dotnet-instrumentation` (dir name misspelled upstream),
> `csharp-type-design-performance`, `csharp-concurrency-patterns`, and `crap-analysis`.
>
> **Why it stopped being a plugin.** The 20 off-stack skills (`akka-*`, `aspire-*`, `playwright-blazor`,
> `mjml-email-templates`, `slopwatch`…) were listed `"off"` in `skillOverrides` for months. That never worked
> and never could — the docs are explicit: **"Plugin skills are not affected by `skillOverrides`. Manage those
> through `/plugin` instead."** Once the plugin was genuinely installed (2026-08-22, after months of being
> declared-but-absent) all 20 went live, and `@backend-dev` was being offered Akka.NET actor-system guidance
> and Blazor Playwright patterns on an Angular + ASP.NET Core project. Vendored loose skills are **not** plugin
> skills, so what sits in `.claude/skills/` is exactly what agents are offered. `skillOverrides` and the
> `dotnet-skills` marketplace entry are both **removed** — they were dead config.
>
> **Trade-off:** frozen at v1.5.0, no auto-update. Refresh instructions and the full skipped-skill list are in
> [.claude/skills/_vendor/README.md](.claude/skills/_vendor/README.md); upstream LICENSE preserved alongside.

> ⚠️ **`enabledPlugins` is a declaration, not an installer.** Adding a key there does **not** fetch the
> plugin — you must also `claude plugin install <name>@<marketplace> --scope project`. This repo has hit
> the trap twice: `dotnet-skills` sat declared-and-documented but uninstalled for months (its marketplace
> was never even registered), and four plugins added on 2026-08-22 landed in the same state before being
> installed properly. **Verify with:** the two sets — `enabledPlugins` in this file and the keys of
> `~/.claude/plugins/installed_plugins.json` — must match exactly, in both directions. `/retro`'s
> setup-drift pass checks this.
>
> **Official Anthropic plugins (project-scoped, in [.claude/settings.json](.claude/settings.json)).** Enabled 2026-08-22 alongside `frontend-design`. They auto-update and are **inert until the session restarts** after enabling:
> - **`claude-code-setup`** — `/claude-automation-recommender`. A read-only scan of the repo that recommends hooks/skills/agents/MCP servers. Calibrated for projects with little setup, so most of its output is already-have here; its yield is *defects in existing config*. The 2026-08-22 run found the two that produced `ClaudeMdAccuracyTests` and ISSUE-389. Run it after a significant setup change, not on a cadence — the recurring version lives in `/retro`'s setup-drift pass.
> - **`claude-md-management`** — `/revise-claude-md` + `claude-md-improver`. Audits CLAUDE.md quality and folds session learnings back in. **Complements, does not duplicate, [ClaudeMdAccuracyTests](src/backend/HRM.Tests/Unit/ClaudeMdAccuracyTests.cs):** the test catches *mechanical* drift (dead links, missing scripts, the ISSUE-312 warning); this catches *prose* rot, which no test can. Neither is sufficient alone.
> - **`pr-review-toolkit`** — six review agents. **`silent-failure-hunter`** and **`type-design-analyzer`** are the net-new ones: they map onto this repo's two documented defect classes (swallowed errors; blind `as` casts hiding contract drift — see BUG-311/BUG-127). `code-reviewer` / `code-simplifier` overlap `/code-review` and `/simplify`; `pr-test-analyzer` overlaps `@test-authenticator`. Prefer the local agents where they overlap — they know the tenant-isolation rules.
> - **`hookify`** — `/hookify` + `conversation-analyzer`, generates hooks from observed friction. Lower marginal value here (11 custom hooks already hand-written with rationale), but useful as the *authoring* step for whatever `/retro`'s skill-friction pass proposes turning into structural enforcement.
> - **`security-guidance`** — pattern warnings on edit, an LLM diff review on `Stop`, and an agentic commit reviewer. Heavy overlap with `secret-guard`, `gitleaks.yml`, `semgrep.yml` and `/security-audit`; the **`Stop`-time diff review is the net-new layer**. Watch for duplicate findings — if it just re-reports what semgrep already caught, mute it via `skillOverrides` rather than living with the noise.
>
> - **`code-review` · `code-simplifier` · `csharp-lsp` · `typescript-lsp` · `feature-dev` · `skill-creator` · `superpowers`** — installed earlier but **undeclared** until 2026-08-22, so they existed only on one machine and never reached CI or a fresh clone. Now declared. `csharp-lsp`/`typescript-lsp` give real language-server navigation over 2,378 C# and 781 TypeScript files and are the highest-value pair of the group.
>
> **Overlap is the thing to manage here, not coverage.** Five plugins landed at once on a repo that already had 12 agents and 23 skills; the failure mode is three reviewers reporting the same finding and nobody reading any of them. Review after one full cycle and mute what does not earn its place.

> **Optional — Angular reference skills.** The Angular team's official [`angular/skills`](https://github.com/angular/skills) package (`npx skills add https://github.com/angular/skills`) gives `@frontend-dev` current, idiomatic Angular reference knowledge — `angular-developer` (signals/`linkedSignal`/`resource`, standalone components, forms, DI, routing, SSR, a11y, testing) and `angular-new-app`. It tracks the latest Angular, matching our Angular 20 + signals + OnPush stack, and is **version-aware** (its rule #1 makes the agent check the project's Angular version before applying guidance — e.g. Signal Forms is gated to v21+, so it won't force v21 features on our v20). The frontend counterpart to `dotnet-skills` above. (Note: prefer this over the now-deprecated `analogjs/angular-skills`.) **`angular-developer` is now vendored** (see below); `angular-new-app` was skipped as greenfield `ng new` scaffolding, irrelevant to our existing app.

> **Vendored loose skills (`.claude/skills/`, manual-update — FROZEN).** Unlike the marketplace plugins above, these third-party MIT skills have **no marketplace manifest**, so they are **copied into the repo** and pinned at the vendored version — they do **not** auto-update. To refresh, re-copy from upstream (there is no auto-update path for loose skills):
> - **`karma-skill`** — [`.claude/skills/karma-skill/`](.claude/skills/karma-skill/), from [`LambdaTest/agent-skills`](https://github.com/LambdaTest/agent-skills). Angular-aware Karma + Jasmine unit-test patterns (`TestBed`, `ComponentFixture`, `HttpTestingController`, `fakeAsync/tick/flush`, `createSpyObj`) matching our Angular 20 + Karma/Jasmine FE test stack. (The sibling `jasmine-skill` was **deliberately not** vendored — generic Jasmine, no Angular, redundant with baseline knowledge.)
> - **`excalidraw-diagram`** — [`.claude/skills/excalidraw-diagram/`](.claude/skills/excalidraw-diagram/), from [`coleam00/excalidraw-diagram-skill`](https://github.com/coleam00/excalidraw-diagram-skill). Generates architecture/flow diagrams as `.excalidraw` JSON for `docs/`, ADRs, and the Obsidian vault. Diagram generation has **zero deps**; the optional PNG self-render/validation pipeline (`references/render_excalidraw.py`) needs Python `uv` + a Chromium and is intentionally **not** set up. Brand colors live in `references/color-palette.md`.
> - **`angular-developer`** — [`.claude/skills/angular-developer/`](.claude/skills/angular-developer/), from the official [`angular/skills`](https://github.com/angular/skills) (Google/Angular team, MIT). SKILL.md + **37 reference files** covering components, signals/`linkedSignal`/`resource`, DI, routing, reactive + signal forms, SSR/rendering strategies, testing (fundamentals + harnesses + e2e + router), ARIA, animations, CLI, and **Tailwind** — the reference brain for `@frontend-dev`. Version-aware (checks project Angular version first). Only `angular-developer` was vendored; `angular-new-app` (greenfield scaffolding) was skipped.

## Why agent-memory is tracked in git (2026-08-22)

> **The rule had quietly inverted.** `.claude/agent-memory/` was gitignored, so 107 of the project's 141
> notes (76%) sat on one NTFS drive — never reviewed, never shared, one disk failure from gone — while
> `docs/vault/` fell from 70 commits in June 2026 to 5 in August. `vault-compliance-advisor` accepted
> *either* store, so a private note satisfied the contract and the shared vault starved. Both are fixed:
> agent-memory is tracked, and the hook now nudges separately on **`private-only`** runs. Replaying 10 real
> subagent transcripts: **3 nudges before, 10 after** — including a `backend-dev` run that touched 55 files
> and left nothing shared.
