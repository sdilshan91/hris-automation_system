# HRM SaaS Automation System

## Project Overview
Multi-tenant HRM SaaS platform built with **Angular 20 + ASP.NET Core 10 + PostgreSQL**.
Reference: `docs/hrm_technical_document_v4.0.md`
Repo: `sdilshan91/hris-automation_system`

## Engineering Discipline (how every agent should work)

These behavioral rules apply to **all** agents and skills, in addition to the
project rules below. They exist to cut wasted diff, rework, and late surprises.

1. **Think before coding — ask when unsure, and seek the best approach.** Don't
   assume. Whenever you have doubts or low confidence about any task — while
   **planning, checking, or executing** — pause and ask clarifying questions, and
   pair each question with **your recommendation**. Surface tradeoffs and name
   competing interpretations instead of silently picking one. Proactively propose a
   **better way, method, or technology** when you see one, and converge on the best
   approach *before* you plan or execute. Don't hide confusion; a question — or a
   better idea — up front is cheaper than a rewrite after.
2. **Simplicity first.** Write the minimal code that solves the stated problem.
   No speculative abstractions, unrequested flexibility, or error handling for
   impossible cases. Self-check: *would a senior engineer call this overcomplicated?*
3. **Surgical changes.** Touch only what the task requires; match adjacent style.
   Clean up only the mess **your** change created (e.g. imports/vars *your* edit
   orphaned) — don't refactor pre-existing dead code as a side quest. When a change
   forces you to touch files outside the task's scope, **flag it explicitly** rather
   than burying it. *Carve-out:* the `/implement-all` **remediation loop** is allowed
   to edit sibling tests/contracts when a verified failure demands it — but it still
   may never weaken, skip, or delete a test to go green.
4. **Goal-driven execution.** Turn each task into verifiable success criteria
   (build passes, tests green, AC met) before starting; for multi-step work keep a
   short checkpointed plan. Strong criteria are what let the agent loop unattended;
   "make it work" is not a success criterion.
5. **Delegate multi-file reading to sub-agents.** When answering a question or
   scoping a task requires reading across several files (search, "how does X work",
   tracing a flow, surveying naming conventions), dispatch an `Explore` /
   `general-purpose` sub-agent and keep the *conclusion*, not the raw file dumps —
   don't pull every file into the main context. For a single-fact lookup where you
   already know the file/symbol, read it directly; don't over-delegate trivia.
   *Parallelism:* run independent sub-agents concurrently (multiple `Agent` calls in
   one message) to speed things up — but **never parallelize dependent steps**
   (where one's output feeds the next) **or concurrent writes to the same file**
   (use `isolation: worktree` if parallel edits are unavoidable).

## Advisor Stance (how to talk to the user)

The user wants a **candid advisor, not an agreeable assistant** — pushback over
comfort. These rules govern *communication and recommendations*, not task execution
(an implementation sub-agent still just builds the story; it applies this when
reporting risks, not by narrating confidence on every line).

- **Lead with the truth, including the uncomfortable part.** If a request rests on a
  bad idea, say so up front — don't bury it after praise.
- **Challenge assumptions.** Name a flawed premise instead of silently executing it;
  surface the tradeoff the user didn't ask about.
- **Rate confidence on non-obvious claims** (e.g. *Confidence: 75%*) so the user can
  calibrate how far to trust them.
- **Say when the user is wrong** — with the reason and evidence, not just the verdict.
- **No empty validation.** Cut "You're absolutely right", "Great question", and
  reflexive agreement. Agree only after checking, and then say *why*, briefly.
- **Honesty over contrarianism.** Do NOT manufacture disagreement to look critical —
  that is just inverse sycophancy. When the user is right, say so plainly and move on.
  The goal is an accurate signal, not a negative one.

## Execution Modes

| Mode | Command | Requires | Best For |
|------|---------|----------|----------|
| **Local + MCP** | `/orchestrate` | Claude Code + GITHUB_TOKEN | Day-to-day development (no API credits needed) |
| **GitHub Actions** | `/github-pipeline` | ANTHROPIC_API_KEY secret + credits | Fully autonomous CI/CD |

**Recommended:** Use **Local + MCP** mode. Agents run in your Claude Code session and push to GitHub via MCP server. No Anthropic API credits needed.

## MCP Server Integration

> **All MCP servers are defined in [`.mcp.json`](.mcp.json)** at the repo root (project scope) —
> this is the file Claude Code actually loads. The `mcpServers` key in `.claude/settings.json` is
> **not** read by the VS Code extension; keep MCP server definitions in `.mcp.json` only. After
> editing `.mcp.json`, fully restart the Claude Code session (a plain "Reload Window" may not
> reconnect) and approve the project-MCP trust prompt.

### GitHub MCP Server
Connected via `https://api.githubcopilot.com/mcp/` (defined in `.mcp.json`)

Enables agents to directly:
- Create feature branches per agent per module
- Push code directly to branches
- Open PRs with story/test references
- Create GitHub Issues for tracking and integration review

**Setup:** `GITHUB_TOKEN` env var from `.env` file (PAT with `repo`, `workflow`, `issues`, `pull_requests` scopes)

### Playwright MCP Server (Browser Debugging)
Local stdio server (`npx @playwright/mcp@latest`) that gives agents a **real Chrome browser** for
runtime investigation of the Angular UI and its calls to the .NET API. Defined in `.mcp.json`
with `--browser chrome --caps vision,pdf,devtools --save-session --output-dir .playwright-artifacts`.
(Note: `--save-session`, not the older `--save-trace`, which current `@playwright/mcp` rejects and
which crashes the server on launch.)

Enables agents to:
- Navigate the running app and reproduce user flows (click, type, fill forms)
- Read **browser console** messages — JS/Angular errors (`browser_console_messages`)
- Inspect **network requests** — status, headers, payloads, CORS (`browser_network_requests`)
- Capture the accessibility snapshot, run page JS (`browser_evaluate`), take screenshots
- Diagnose auth / **multi-tenant** routing issues from real traffic

**Activation:** the server connects at Claude Code session startup from `.mcp.json`. After first
adding/changing it, **fully restart the Claude Code session** (a plain VS Code "Reload Window" on an
already-running session may not reconnect) and **approve the project-MCP trust prompt**, then confirm
`playwright` is connected (e.g. via `/mcp` where available). Artifacts (session/screenshots) save to
`.playwright-artifacts/` (gitignored). It is **read-only on the codebase** — used to investigate, not
to edit code. Driven by the `@browser-debugger` agent and the `/debug-ui` skill.

### Chrome DevTools MCP Server (performance / Lighthouse / memory)
Local stdio server (`npx chrome-devtools-mcp@latest --isolated`, defined in `.mcp.json`) that exposes the
**Chrome DevTools Protocol** — the front-end performance + audit layer Playwright doesn't cover. It launches
its **own isolated Chrome** (separate from the Playwright instance). Enables agents to:
- Run a one-shot **`lighthouse_audit`** (performance + accessibility + best-practices) — a second a11y signal alongside `@axe-core/playwright`.
- Record runtime **performance traces** (`performance_start_trace` → `performance_stop_trace` → `performance_analyze_insight`) for Core Web Vitals (LCP/CLS/TBT).
- **Throttle** CPU / network (`emulate`) to reproduce slow conditions; **`take_heapsnapshot`** for memory leaks.
- Inspect network/console under CDP (`list_network_requests`, `get_network_request`, `list_console_messages`).

**Division of labour:** **Playwright MCP** = functional UI, a11y (axe), cross-browser, DOM/console/network;
**Chrome DevTools MCP** = *why is it slow / leaking / failing a Lighthouse audit*; **k6** = server/load perf.
Same activation rules as Playwright (attaches at session startup from `.mcp.json`; needs a full restart +
trust-prompt approval). Used by `@test-runner` (perf/a11y TC execution) and `@browser-debugger` (deep
diagnosis). **Read-only on the codebase.**

## Agent Team

| Agent | Role | Branch | MCP Tools |
|-------|------|--------|-----------|
| `@business-analyst` | Analyzes docs → IEEE 830 user stories | `feature/user-stories-{module}` | create_issue, create_branch, push_files, create_pull_request |
| `@frontend-dev` | Implements Angular 20 UI | `feature/frontend-{module}` | create_branch, push_files, create_pull_request |
| `@backend-dev` | Implements ASP.NET Core 10 API | `feature/backend-{module}` | create_branch, push_files, create_pull_request |
| `@qa-engineer` | Writes IEEE 829 test cases | `feature/qa-{module}` | create_branch, push_files, create_pull_request, create_issue |
| `@browser-debugger` | Drives Chrome to debug UI (console, network, DOM) — read-only investigator | _(no branch — diagnoses only)_ | playwright (navigate, console_messages, network_requests, snapshot, evaluate, screenshot, interactions) + chrome-devtools (lighthouse, perf-trace, heapsnapshot, emulate) |
| `@test-runner` | **Executes** test cases against the running stack + **triages** findings (bug/issue/enhancement: severity, root cause, repro). **REPORT-ONLY — never fixes, never opens PRs.** Writes only to `test-cases/` ledgers. | _(no branch — diagnoses only)_ | playwright (UI/a11y/cross-browser) + chrome-devtools (lighthouse/perf-trace/memory) + create_issue (optional); runs xUnit/Karma/Playwright/axe/k6/curl via Bash |
| `@test-authenticator` | **Read-only auditor** of test quality — flags "test theater" (mock-everything, tautologies, happy-path-only, InMemory-masks-Postgres, fake isolation arms). Reports a verdict; **never edits/weakens a test.** Use after test code changes. | _(no branch — review only)_ | _none (read-only: Read/Glob/Grep/Bash)_ |
| `@integration-enforcer` | **Read-only auditor** of wiring — catches orphaned code (undispatched MediatR handlers, missing DI, unrouted Angular components, entities missing tenant query filters). Reports a verdict; **never wires it itself.** Use after implementation. | _(no branch — review only)_ | _none (read-only: Read/Glob/Grep/Bash)_ |

> The last two are **auxiliary local review agents** in [`.claude/agents/review/`](.claude/agents/review/)
> (adapted from third-party MIT agent definitions, retargeted to this stack). They are read-only and
> report-only — separate from the pipeline `team/` agents above; invoke them explicitly or let them
> auto-delegate after dev/test changes.

## Skills (Slash Commands)

| Command | Mode | Description |
|---------|------|-------------|
| `/implement-all [module\|US-ID]` | Local + MCP | **Loop driver.** Picks the next pending story from `user-stories/STATUS.md`, builds it end-to-end (BE + FE + QA in parallel), runs the full verify gate with an autonomous remediation loop, then commits + opens a PR. One story per call; rerun (or `/loop`) to continue. See below. |
| `/orchestrate` | Local + MCP | Full pipeline: BA → (FE + BE + QA in parallel via worktrees) |
| `/analyze-module {name}` | Local + MCP | Generate user stories for a specific module |
| `/research-story US-{ID}` | Local + MCP | **Feasibility gate (RPI-style).** Read-only: reads ONE story + codebase + vault and writes `research/US-{ID}.md` with a GO / GO-WITH-CONDITIONS / NO-GO verdict. Run before implementing a large/risky/unclear story. |
| `/implement-story US-{ID}` | Local + MCP | Implement ONE specific story end-to-end (manual single-shot; does NOT touch STATUS.md) |
| `/test-all [module\|US-ID]` | Local + MCP | **Test loop driver (REPORT-ONLY).** Picks the next untested story from `test-cases/TEST-STATUS.md`, executes its test cases against the running stack via `@test-runner`, and logs bugs/issues/enhancements to `test-cases/TEST-FINDINGS.md` (severity, status, root cause, repro). **Never fixes; never opens PRs.** One story per call; rerun (or `/loop`) to continue. See below. |
| `/test-us US-{ID}` | Local + MCP | Execute the test cases for ONE specific story (manual single-shot; **REPORT-ONLY**; does NOT touch TEST-STATUS.md). |
| `/fix-finding {BUG-ID\|ISSUE-ID}` | Local + MCP | **Finding-driven fix driver.** Fixes ONE finding from `test-cases/TEST-FINDINGS.md` end-to-end (dev agent + a regression TC via `@qa-engineer` + `@test-authenticator`/`@integration-enforcer`/`/security-audit` gates) on one `fix/{ID}` branch + PR. Edits `src/`; **does NOT touch the ledgers** — run `/verify-fix` after merge. The finding-driven counterpart to `/implement-story`. |
| `/verify-fix {BUG-ID\|ISSUE-ID}` | Local + MCP | **Fix close-out.** After a `/fix-finding` PR merges: re-runs the finding's affected TCs via `@test-runner` (TC-scoped, or `--iso` for a cross-module isolation re-run), flips `TEST-STATUS.md`, and marks the finding **RESOLVED** in `TEST-FINDINGS.md` with the PR#. The only skill authorized to close a finding; writes only to `test-cases/`. |
| `/security-audit [scope]` | Local + MCP | **HRM security gate.** Reviews a diff (branch/US-ID/path) against this platform's threat model — tenant isolation, authz, injection, secrets, PII — and writes `security-reviews/{scope}.md` with severity-by-exploitability findings + fixes. Read-only; run before opening a PR. `--deep` fans out parallel reviewers. |
| `/debug-ui {symptom\|URL}` | Local + MCP (Playwright) | Debug the running UI in a real browser — console + network + DOM diagnosis via `@browser-debugger` |
| `/fault-diagnosis` | Local | **Root-cause-before-fix discipline.** 4-phase method (read Serilog by `RequestId` → reproduce → hypothesis → fix the source) + backward call-stack tracing, flaky/order-dependent test bisection (xUnit/Karma), condition-based waiting. Encodes this repo's known root-cause classes (InMemory-masks-Postgres, BUG-003 tenant split). Respects the **report-only** boundary (diagnosis ends at a finding under `/test-all`). |
| `/error-recovery` | Local | **Stuck-loop breaker.** Failure counter + 2/3/4-attempt escalation (Yellow→Orange→Red), "fix the code not the test," rollback-to-known-good. Governs each attempt *inside* the `/implement-all` 3-attempt remediation cap; pairs with `/fault-diagnosis`. |
| `/github-pipeline {module}` | GitHub Actions | Trigger remote pipeline (needs API credits) |

> **Locally-vendored discipline skills.** `/fault-diagnosis` and `/error-recovery` live in
> [`.claude/skills/`](.claude/skills/) (adapted from third-party MIT skill definitions, retargeted to
> this stack — Serilog/`RequestId`, EF/Postgres, xUnit/Karma/Playwright). They are guidance protocols,
> not pipeline drivers; invoke them explicitly or let them fire on bug/stuck-loop triggers. They defer to
> the `test-integrity-guard` hook and the `/implement-all` remediation loop rather than competing with them.

> **Optional — .NET reference skills.** Installing the third-party MIT-licensed [`dotnet-skills`](https://github.com/Aaronontheweb/dotnet-skills) plugin (`/plugin marketplace add Aaronontheweb/dotnet-skills`) gives `@backend-dev` battle-tested C#/EF Core reference knowledge. Lean on **`efcore-patterns`** (NoTracking-by-default, query splitting, CLI-only migrations — reinforces our "never hand-write migrations" rule), **`testcontainers`** (our integration-test approach), `database-performance`, `csharp-api-design`/`-coding-standards`, and the `microsoft-extensions-*` DI/config skills. Off-stack skills (`akka-*`, `aspire-*`, `playwright-blazor`/`-ci-caching`, `mjml-email-templates`, `verify-email-snapshots`, `r3-reactive-extensions`, `ilspy-decompile`, `dotnet-devcert-trust`, `local-tools`, `marketplace-publishing`, `skills-index-snippets`, `slopwatch`) are muted via `skillOverrides` in [.claude/settings.json](.claude/settings.json). As of v1.4.1 the plugin ships **35 skills + 6 agents**; keep the mute list in sync when it grows. Installed as a **project-scoped** plugin that auto-updates — its `extraKnownMarketplaces` + `enabledPlugins` live in the project [.claude/settings.json](.claude/settings.json) (not global), so it activates in this repo and travels with it. Not vendored.

> **Optional — Angular reference skills.** The Angular team's official [`angular/skills`](https://github.com/angular/skills) package (`npx skills add https://github.com/angular/skills`) gives `@frontend-dev` current, idiomatic Angular reference knowledge — `angular-developer` (signals/`linkedSignal`/`resource`, standalone components, forms, DI, routing, SSR, a11y, testing) and `angular-new-app`. It tracks the latest Angular, matching our Angular 20 + signals + OnPush stack, and is **version-aware** (its rule #1 makes the agent check the project's Angular version before applying guidance — e.g. Signal Forms is gated to v21+, so it won't force v21 features on our v20). The frontend counterpart to `dotnet-skills` above. (Note: prefer this over the now-deprecated `analogjs/angular-skills`.) **`angular-developer` is now vendored** (see below); `angular-new-app` was skipped as greenfield `ng new` scaffolding, irrelevant to our existing app.

> **Vendored loose skills (`.claude/skills/`, manual-update — FROZEN).** Unlike the marketplace plugins above, these third-party MIT skills have **no marketplace manifest**, so they are **copied into the repo** and pinned at the vendored version — they do **not** auto-update. To refresh, re-copy from upstream (there is no auto-update path for loose skills):
> - **`karma-skill`** — [`.claude/skills/karma-skill/`](.claude/skills/karma-skill/), from [`LambdaTest/agent-skills`](https://github.com/LambdaTest/agent-skills). Angular-aware Karma + Jasmine unit-test patterns (`TestBed`, `ComponentFixture`, `HttpTestingController`, `fakeAsync/tick/flush`, `createSpyObj`) matching our Angular 20 + Karma/Jasmine FE test stack. (The sibling `jasmine-skill` was **deliberately not** vendored — generic Jasmine, no Angular, redundant with baseline knowledge.)
> - **`excalidraw-diagram`** — [`.claude/skills/excalidraw-diagram/`](.claude/skills/excalidraw-diagram/), from [`coleam00/excalidraw-diagram-skill`](https://github.com/coleam00/excalidraw-diagram-skill). Generates architecture/flow diagrams as `.excalidraw` JSON for `docs/`, ADRs, and the Obsidian vault. Diagram generation has **zero deps**; the optional PNG self-render/validation pipeline (`references/render_excalidraw.py`) needs Python `uv` + a Chromium and is intentionally **not** set up. Brand colors live in `references/color-palette.md`.
> - **`angular-developer`** — [`.claude/skills/angular-developer/`](.claude/skills/angular-developer/), from the official [`angular/skills`](https://github.com/angular/skills) (Google/Angular team, MIT). SKILL.md + **37 reference files** covering components, signals/`linkedSignal`/`resource`, DI, routing, reactive + signal forms, SSR/rendering strategies, testing (fundamentals + harnesses + e2e + router), ARIA, animations, CLI, and **Tailwind** — the reference brain for `@frontend-dev`. Version-aware (checks project Angular version first). Only `angular-developer` was vendored; `angular-new-app` (greenfield scaffolding) was skipped.

### `/implement-all` — autonomous story loop

Source of truth: [.claude/skills/implement-all.md](.claude/skills/implement-all.md). Per story it:

1. Picks the first `[ ]` story in `user-stories/STATUS.md` (scoped by module/ID arg, else priority order), marks it `[~]`, and cuts `feature/US-{MODULE}-{NNN}` from fresh `main`.
2. Runs `@backend-dev` (incl. DB/EF/migrations), `@frontend-dev`, and `@qa-engineer` **in parallel** on non-overlapping paths; sub-agents do **not** commit.
3. **Verify gate:** `dotnet build` → `dotnet test` → `npm run build` → `ng test` (headless). Any failure enters the **remediation loop** — up to 3 attempts that hand the verbatim errors to the owning dev agent and re-run the whole gate. It may **never** weaken/skip a test to go green; if it can't fix cleanly in 3 attempts it reverts the story to `[ ]` and stops without a PR.
4. On green: commits `feat(US-XXX)`, pushes, opens a PR, flips STATUS.md `[~]`→`[x]` on `main`.

Run continuously with `/loop /implement-all [scope]` — it re-fires until the scope reports "all done." Requires a **clean working tree on `main`**; only run unattended when you're willing to review the stacked PRs after the fact (they are opened, not auto-merged).

### `/test-all` — autonomous test-execution loop (REPORT-ONLY)

Source of truth: [.claude/skills/test-all.md](.claude/skills/test-all.md). The **testing** counterpart to
`/implement-all`. **Hard policy decision: the testing loop identifies and documents defects but NEVER fixes
them.** It has **no remediation loop** — a failing test produces a *finding*, not a fix attempt. Fixing is a
separate step the human decides on after reviewing the ledger. Per story it:

1. Picks the first `[ ]` (not-tested) story in [test-cases/TEST-STATUS.md](test-cases/TEST-STATUS.md) (scoped by module/ID arg, else priority order), pre-flights the running stack, marks it `[~]`.
2. Dispatches `@test-runner` to execute every test case bound to that story — bound automated test (xUnit/Karma/Playwright) if present, else API-layer (curl + JWT) / UI-layer (Playwright MCP) per the TC steps.
3. Records each TC verdict (flips the TC `status:` `draft → automated → pass | fail | blocked`) and appends **every** defect to [test-cases/TEST-FINDINGS.md](test-cases/TEST-FINDINGS.md) with the full schema: **type** (BUG/ISSUE/ENH), **severity**, **status** (`OPEN`), **layer**, module/US/TC, **root cause + confidence**, **reproduction steps**, and evidence.
4. Flips TEST-STATUS.md: `[x]` tested-clean · `[!]` tested-with-findings (lists the finding IDs) · `[b]` blocked.

`@test-runner` writes **only** to `test-cases/` ledgers — it must never edit `src/`, never weaken a test to
go green, and never open a PR. Run continuously with `/loop /test-all [scope]`; because nothing is auto-fixed
and no PRs are opened, this is **safe to run unattended** — the worst case is a longer findings ledger to
triage. `/test-us US-{ID}` is the manual single-shot variant (does not touch TEST-STATUS.md). The findings in
`TEST-FINDINGS.md` are the input to a **separate, human-decided** fix cycle (e.g. you then run `/implement-story`
or hand a finding to a dev agent).

## Automation Hooks

| Hook | Trigger | Action |
|------|---------|--------|
| `post-user-story-commit` | User story files committed | Notifies dev + QA agents to start |
| `post-dev-commit` | Frontend/backend code committed | Notifies QA to review test cases |
| `sound notifications` | `Stop`, `Notification`, `PermissionRequest`, `SubagentStop` | Plays a short sound via `python .claude/hooks/scripts/hooks.py` so you know when a long `/implement-all` run finishes or needs you. Toggle per-hook in `.claude/hooks/config/hooks-config.json` (or git-ignored `…local.json`); disable all via `disableAllHooks` in `settings.local.json`. Needs Python 3. |
| `secret-guard` | `PreToolUse` on `Write\|Edit` | **Enforces** Critical Rule #6. Blocks a write whose *pending* content contains a hardcoded secret (Postgres `Password=…`, DB connection URLs with creds, `Jwt:PrivateKey`, private-key blocks, GitHub/AWS tokens, JWTs). Exempts gitignored secret files (`.env`, `*.local.json`). Fails open. Override for one run with `CLAUDE_DISABLE_SECRET_GUARD=1`. |
| `test-integrity-guard` | `PreToolUse` on `Write\|Edit` | **Enforces** the "never weaken/skip/delete a test to go green" rule. Blocks edits to test files (`*.spec.ts`, `*Tests.cs`, …) that introduce skip/focus markers (`xit`/`fit`/`.skip`/`.only`/`[Fact(Skip)]`/`[Ignore]`) or remove test cases. Fails open. Override with `CLAUDE_DISABLE_TEST_GUARD=1`. |

## Pipeline Flow (Local + MCP)

```
                      LOCAL (Claude Code)              GITHUB (via MCP)
                      ──────────────────               ────────────────
[docs/]
   │
   ▼
@business-analyst ─────────────────────── MCP ──► branch: feature/user-stories-{module}
   │  (writes user-stories/)                      PR: "IEEE 830 stories for {module}"
   │                                              Issues: epic per module
   │
   ├── Stage 2 (parallel via git worktrees) ──┐
   │                                          │
   ▼                 ▼                        ▼
@frontend-dev   @backend-dev           @qa-engineer
   │                 │                        │
   MCP               MCP                     MCP
   ▼                 ▼                        ▼
branch + PR      branch + PR             branch + PR
(Angular 20)     (.NET Core 10)          (test cases)
   │                 │                        │
   └─────────────────┼────────────────────────┘
                     ▼
           GitHub: Integration Review Issue
```

## Branch Strategy

```
main
├── feature/user-stories-{module}   ← @business-analyst
├── feature/frontend-{module}       ← @frontend-dev (worktree)
├── feature/backend-{module}        ← @backend-dev  (worktree)
└── feature/qa-{module}             ← @qa-engineer  (worktree)
```

## Directory Structure

```
├── .env                           # API keys (gitignored, local only)
├── .env.example                   # Template for .env
├── .mcp.json                      # MCP server definitions (github, playwright) — loaded by Claude Code
├── .gitignore
├── docs/                          # Technical documentation (source of truth)
│   └── vault/                     # Obsidian vault — shared agent memory (see Shared Memory section)
├── user-stories/                  # IEEE 830 user stories (by module)
│   ├── {module-name}/
│   │   └── US-{MOD}-001.md
│   └── INDEX.md
├── test-cases/                    # IEEE 829 test cases (by module)
│   ├── {module-name}/
│   │   ├── TC-{MOD}-001.md
│   │   └── TEST-MATRIX.md
│   ├── TRACEABILITY-MATRIX.md
│   └── TEST-PLAN.md
├── src/
│   ├── frontend/                  # Angular 20 SPA
│   └── backend/                   # ASP.NET Core 10 API
├── .claude/
│   ├── agents/team/               # Agent definitions (with MCP tools)
│   │   ├── business-analyst.md
│   │   ├── frontend-dev.md
│   │   ├── backend-dev.md
│   │   ├── qa-engineer.md
│   │   └── browser-debugger.md    # Playwright-driven UI debugger (read-only)
│   ├── agents/review/             # Auxiliary read-only review agents (local, adapted)
│   │   ├── test-authenticator.md  # Flags fake/theatrical tests (report-only)
│   │   └── integration-enforcer.md # Flags orphaned/unwired code (report-only)
│   ├── skills/                    # Slash command skills
│   │   ├── orchestrate.md         # Local + MCP pipeline
│   │   ├── analyze-module.md
│   │   ├── implement-story.md
│   │   ├── debug-ui.md            # Browser debugging via Playwright MCP
│   │   ├── fault-diagnosis.md     # Root-cause-before-fix discipline (local)
│   │   ├── error-recovery.md      # Stuck-loop breaker / failure-counter (local)
│   │   └── github-pipeline.md     # Remote pipeline (needs credits)
│   ├── hooks/                     # Automation hooks
│   │   ├── post-user-story-commit.sh
│   │   └── post-dev-commit.sh
│   └── settings.json              # hooks, permissions, skill overrides (NOT MCP servers — see .mcp.json)
└── .github/
    └── workflows/
        └── claude-agent-pipeline.yml  # GitHub Actions (future, needs credits)
```

## Shared Memory (Obsidian Vault)

All agents share a persistent markdown knowledge base at `docs/vault/`. Open as an Obsidian vault for the human view; agents read/write the `.md` files directly. Start at [docs/vault/Home.md](docs/vault/Home.md) and follow conventions in [docs/vault/README.md](docs/vault/README.md).

| Folder | Use it for |
|---|---|
| `docs/vault/agents/{agent}.md` | Per-agent persistent notes (preferences, gotchas, working patterns) |
| `docs/vault/modules/{module}.md` | Domain rules, edge cases, why-decisions per module |
| `docs/vault/decisions/` | ADR-lite architecture/design decisions |
| `docs/vault/handoffs/` | Short-lived context drops between agents in a pipeline run |
| `docs/vault/incidents/` | Bug/incident post-mortems |

**Agent contract:**
- Before starting work on a module, check `docs/vault/modules/{module}.md` and your own `docs/vault/agents/{agent}.md` for prior context.
- When you make a non-obvious decision or learn a domain rule worth keeping, write it to the appropriate vault folder (not into the code as a comment).
- When handing off to another agent in the same run, drop a note in `docs/vault/handoffs/` with frontmatter `from:` and `to:`.
- Use Obsidian wiki links `[[note-name]]` between vault notes so backlinks work.
- Never put secrets, generated logs, or transient task state in the vault.

### Vault vs. built-in agent memory

There are **two** distinct memory stores — keep them separate so knowledge doesn't fragment:

| Store | What it is | Use for |
|---|---|---|
| **Obsidian vault** (`docs/vault/`) | Manual, **shared**, human-browsable. The cross-agent source of truth. | Domain rules, ADRs, handoffs, anything another agent/human should read. |
| **Built-in agent memory** (`.claude/agent-memory/{agent}/`) | Auto-loaded each run via `memory: project` in agent frontmatter. **Private to that one agent.** | An agent's own operational notes ("tried X, it failed", recurring gotchas) it wants auto-recalled next run. |

Rule of thumb: if it's worth sharing, it goes in the **vault**; if it's just one agent's working memory, the built-in store is fine. Never duplicate the same fact into both. Secrets/logs go in neither.

## Critical Rules
1. **Tenant isolation is non-negotiable** — every query, cache key, and API call must be tenant-scoped
2. **IEEE standards** — user stories follow IEEE 830, test cases follow IEEE 829
3. **Parallel execution** — dev agents and QA agent run simultaneously via git worktrees
4. **Traceability** — every test case must link back to a user story and acceptance criteria
5. **MCP-first** — prefer GitHub MCP tools over manual git commands for branch/PR/issue operations
6. **Secrets in .env only** — never hardcode tokens, always use `${ENV_VAR}` references

## Module Priority
1. Authentication & Authorization
2. Core HR (Employees, Departments, Org Tree)
3. Leave Management
4. Attendance
5. Recruitment
6. Payroll
7. Performance Management
8. Admin Console (System + Tenant)
9. Onboarding/Offboarding
10. Training & Benefits
11. Reports & Analytics
12. Notifications & Audit

---

# Application Development

> The sections above describe the **agent-orchestration meta-system**. The sections below describe the **actual HRM application** in `src/` — how to build, run, and test it, and how its architecture fits together.

## Commands

### Backend (`src/backend`, .NET 10)
```bash
dotnet restore HRM.sln
dotnet build HRM.sln
dotnet run --project HRM.Api          # serves API + Swagger UI at /swagger, Hangfire dashboard at /hangfire (dev only)

# EF Core migrations (run from src/backend; --startup-project supplies config/connection string)
dotnet ef migrations add <Name> --project HRM.Infrastructure --startup-project HRM.Api
dotnet ef database update --project HRM.Infrastructure --startup-project HRM.Api
```
Migrations are **applied automatically on startup** via `DbInitializer.RunAsync` (`Program.cs`), which also seeds a default admin tenant, roles, and admin user. There is currently no backend test project.

### Frontend (`src/frontend`, Angular 20)
```bash
npm install
npm start            # ng serve — dev server
npm run build        # ng build
npm test             # ng test — Karma + Jasmine (single project, headful Chrome)
npm run lint         # ng lint
ng test --include='**/auth.service.spec.ts'   # run a single spec
```

## Local Configuration (required to run)
`appsettings.json` ships with **blank secrets** — the app will not start until these are set, ideally via .NET user-secrets (`UserSecretsId` is already in `HRM.Api.csproj`), not by editing the committed file:
- `ConnectionStrings:DefaultConnection` — PostgreSQL (`Password` is empty in the template)
- `Jwt:PrivateKey` — signing key for JWT validation
- A running **PostgreSQL** instance (also backs Hangfire job storage)

## Architecture

### Backend: Clean Architecture + CQRS
Four projects, dependencies point inward (`Api → Application → Domain`; `Infrastructure → Application`):
- **HRM.Domain** — entities, value objects (e.g. `Email`), repository interfaces. No framework dependencies.
- **HRM.Application** — CQRS handlers organized by feature (`Features/{Feature}/Commands|Queries|DTOs|Validators`), MediatR pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`), and `Common/Interfaces` abstractions (`ITenantContext`, `ICurrentUser`, `IJwtService`, `IAuthService`).
- **HRM.Infrastructure** — EF Core `AppDbContext`, entity configurations, interceptors, and interface implementations. Wired up in `DependencyInjection.AddInfrastructure`.
- **HRM.Api** — controllers (thin; dispatch via MediatR), middleware, filters, Hangfire jobs. Composition root is `Program.cs`.

Request flow: validation runs both via the MVC `ValidationFilter` and the MediatR `ValidationBehavior`; `ExceptionHandlingMiddleware` is the outermost layer and normalizes errors.

### Multi-Tenancy (the central architectural concern)
Tenant isolation is enforced in **three coordinated layers** — when adding entities or queries, all three matter:
1. **Resolution** (`TenantResolutionMiddleware`, runs before auth): extracts tenant from the request **subdomain** (`acme.yourhrm.com` → `acme`; `admin.*` → system context; reserved subdomains skip resolution). Looks up the tenant and populates the scoped `ITenantContext`. Dev fallback: the SPA sends an `X-Tenant-Subdomain` header (set by the frontend `tenantInterceptor`) so `*.localhost` hosts-file entries aren't needed.
2. **Write isolation** (`TenantInterceptor`, a `SaveChanges` interceptor): auto-stamps `TenantId` on any new `BaseEntity` when a tenant is resolved.
3. **Read isolation** (global query filters in `AppDbContext.OnModelCreating`): every tenant-scoped entity is filtered by `TenantId == _tenantContext.TenantId`. Use `IgnoreQueryFilters()` only deliberately (e.g. the tenant lookup in the resolution middleware itself).

`AuditInterceptor` similarly stamps audit fields. EF uses PostgreSQL with **snake_case** naming convention (`EFCore.NamingConventions`).

### Cross-cutting backend infrastructure
- **Auth**: JWT bearer; `JwtService` is registered as a singleton and also supplies `TokenValidationParameters`. BCrypt for password hashing. Refresh tokens are cleaned up by the `TokenCleanupJob` Hangfire recurring job (daily).
- **Background jobs**: Hangfire on PostgreSQL storage; dashboard at `/hangfire` (dev only).
- **Resilience**: a named `ResilientClient` HttpClient with Polly retry + circuit-breaker for outbound calls.
- **Logging**: Serilog; `TenantId`/`TenantSubdomain`/`RequestId` are pushed into the log context per request. Writes a daily rolling **structured file** at `src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log` (console + file sinks; exception + stack included). In **Development** (`appsettings.Development.json`) the level is raised for root-causing: `HRM.*` at **Debug** and **EF Core SQL** (`Microsoft.EntityFrameworkCore.Database.Command`) at Information — base `appsettings.json` stays Information-only for prod. **QA/debug practice:** `@test-runner` and `@browser-debugger` read this log (correlating by `RequestId`) to pull the real exception/stack/SQL behind a failing TC — never infer root cause from the HTTP body alone when a log line exists. Requires a backend restart after changing the logging config.

### Frontend: standalone Angular 20
- `core/` holds singletons: `auth/` (service, guard, interceptor, models), `interceptors/` (`error`, `tenant`), `tenant/` (subdomain resolution mirroring the backend rules, using signals).
- `features/` holds route-lazy feature components (e.g. `auth/login`, `dashboard`); `layouts/` holds `auth-layout` / `main-layout`.
- HTTP interceptors are functional (`HttpInterceptorFn`). The `tenantInterceptor` injects `X-Tenant-Subdomain` from `environment.tenantSubdomain` for local dev.
- UI stack: Angular Material + Tailwind CSS, ngx-translate (i18n), ngx-toastr (notifications).

### Traceability convention
Code, user stories (`user-stories/`, IEEE 830), and test cases (`test-cases/`, IEEE 829) are cross-referenced by ID — e.g. `US-AUTH-007` appears in both `TenantService` comments and `test-cases/authentication/`. Preserve these references when modifying related code.
