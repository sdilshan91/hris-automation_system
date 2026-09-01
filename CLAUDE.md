# HRM SaaS Automation System

## Project Overview
Multi-tenant HRM SaaS platform built with **Angular 20 + ASP.NET Core 10 + PostgreSQL**.
Reference: `docs/Architecture/hrm_technical_document_v4.0.md`
Repo: `sdilshan91/hris-automation_system`

## Engineering Discipline (how every agent should work)

These behavioral rules apply to **all** agents and skills, in addition to the
project rules below. They exist to cut wasted diff, rework, and late surprises.

1. **Research the challenge, then ask with a recommendation.** Don't assume. When a
   task presents a real challenge, **research it properly before choosing** — compare
   actual options, then rank them and state an explicit **confidence** on each, and decide.
   Whenever you have doubts, a decision to put to the user, a clarification to request, or
   a suggestion — while **planning, checking, or executing** — ask via **`AskUserQuestion`**,
   and pair every question with **your recommendation**. **Recommend the most suitable
   option, never the easiest or the lowest-effort one**: if the right answer is the
   expensive one, say so and defend it — a recommendation optimised for your own effort is
   worse than no recommendation. Surface tradeoffs and name competing interpretations
   instead of silently picking one. Proactively propose a better way, method, or technology
   when you see one, and converge on the best approach *before* you plan or execute.
   A question — or a better idea — up front is cheaper than a rewrite after.
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
   *(As of 2026-08-22 `backend-dev`, `frontend-dev`, `qa-engineer`, `business-analyst` and
   `requirements-auditor` hold the `Agent` tool and can actually do this. The remaining agents
   deliberately cannot — narrow single-pass audits where fan-out adds cost, not coverage.)*
   *Parallelism — default to it.* Where a task splits into independent lanes, run them
   **concurrently** (multiple `Agent` calls in one message) rather than serially; serial
   execution of independent work is a choice you should have to justify. The two hard
   limits stand: **never parallelize dependent steps** (where one's output feeds the next)
   **or concurrent writes to the same file** (use `isolation: worktree` if parallel edits
   are unavoidable).
6. **Auto-heal: never silently drop an out-of-lane discovery.** Work constantly surfaces
   things outside the current task's lane — a new bug, an adjacent-module dependency, a
   broken sibling test, a missing endpoint the FE already calls, a licensing/infra snag.
   **Stay in your lane to *fix*, but never in your lane to *ignore*.** Sub-agents **FLAG**
   these in a structured `OUT-OF-LANE:` block (type · severity · where · what · why-out-of-lane
   · suggested action) and do **not** scope-creep to fix them (a trivial, clearly-correct,
   same-file correction is the only exception, and it's still noted). The **orchestrator HEALS**:
   files the finding to `docs/QA/TEST-FINDINGS.md`, folds it into the **live queue**
   (`docs/QA/plans/GAP-CLOSURE-QUEUE.md` — what the loop actually executes; **not** `COMPLETION-PLAN.md`,
   which the loop stopped reading in 2026-08), and
   **re-sorts the priority order** (severity × blast-radius × unblocks-others; decision/infra-gated
   items park at the decision-gate). The completion plan is a **living document** — it changes every
   time reality does. Protocol: [`/auto-heal`](.claude/skills/auto-heal.md). This does **not** bypass
   the report-only boundary, the test-integrity rule, or the decision-gate — it *tracks and ranks*;
   the human still decides gated work.
   **Mandatory inside any loop or long task** — `/implement-all`, `/test-all`, `/campaign`,
   `/loop` — and it covers **everything** surfaced, not just defects: nice-to-haves and gaps
   are filed too, as `ENH` in the same `TEST-FINDINGS.md` (one ledger keeps the shared ID
   sequence and the de-dup step working; a second file silently breaks both). A discovery
   that only ever appears in a transcript was not tracked. When an out-of-lane finding lands
   at **CRIT or HIGH**, re-order the live task queue on the spot — that finding may
   legitimately outrank the story you are mid-way through; say so in the turn summary rather
   than finishing the lower-value item first out of momentum.

7. **Plan it, track it, and finish the pipeline yourself.** Break every non-trivial task
   into sub-tasks with a real plan *before* starting, and keep a **todo list** you update
   as each sub-task completes — not retroactively at the end. Put the plan to the user when
   an item genuinely needs their input; otherwise proceed. You are **authorized to commit,
   push, open PRs, and merge them** to carry a loop or long task to completion without
   asking each time. That authority is bounded by the **merge gate** in
   [`/pr-pipeline`](.claude/skills/pr-pipeline.md): auto-merge only on a green verify gate
   with no CRIT/HIGH from the three audit agents, and **never** for a PR touching EF
   migrations, auth/JWT, tenant isolation, or CI/hook/settings config — those stay open for
   a human however green they are. *When an unattended loop hits a genuine doubt:* file it
   as a `DECISION` finding, park that item at the decision-gate, and **continue with the
   next unblocked item**. Never halt the whole queue over one ambiguity, and never resolve
   it by quietly guessing — report every parked question in the turn summary.
8. **One session, one worktree, one branch.** Concurrent Claude sessions on this repo must
   not share a working tree. Each takes its **own git worktree on its own branch**
   (`isolation: worktree` for sub-agents) and rebases on fresh `origin/main` before opening
   a PR. For the shared ledgers — `STATUS.md`, `TEST-STATUS.md`, `TEST-FINDINGS.md`,
   `GAP-CLOSURE-QUEUE.md`, `COMPLETION-PLAN.md` — **re-read immediately before every write**: another session may
   have appended since you last looked, and writing back a copy you cached earlier in the
   turn silently deletes their work.

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

Setup steps, capability flags and the plugin-collision history: [docs/DEV/mcp-servers.md](docs/DEV/mcp-servers.md).

| Server | Purpose | Driven by |
|---|---|---|
| **github** | branches, PRs, issues (`${GITHUB_TOKEN}`) | all writing agents |
| **playwright** | functional UI, a11y (axe), DOM/console/network | `@browser-debugger`, `@test-runner` |
| **chrome-devtools** | Lighthouse, perf traces, heap snapshots | `@browser-debugger`, `@test-runner` |
| **microsoft-learn** | grounded .NET/Azure docs | `@principal-advisor`, `@backend-dev` |

> Do **not** install the official `github`, `playwright` or `microsoft-docs` plugins — each declares a
> server with the **same name** but a worse config, and `playwright`'s would silently drop
> `--caps vision,pdf,devtools` and `--save-session`. Uninstalled 2026-08-22; see the doc above.

## Agent Team

| Agent | Role | Branch | MCP Tools |
|-------|------|--------|-----------|
| `@business-analyst` | Analyzes docs → IEEE 830 user stories | `feature/user-stories-{module}` | create_issue, create_branch, push_files, create_pull_request |
| `@frontend-dev` | Implements Angular 20 UI | `feature/frontend-{module}` | create_branch, push_files, create_pull_request |
| `@backend-dev` | Implements ASP.NET Core 10 API | `feature/backend-{module}` | create_branch, push_files, create_pull_request |
| `@qa-engineer` | Writes IEEE 829 test cases | `feature/qa-{module}` | create_branch, push_files, create_pull_request, create_issue |
| `@browser-debugger` | Drives Chrome to debug UI (console, network, DOM) — read-only investigator | _(no branch — diagnoses only)_ | playwright (navigate, console_messages, network_requests, snapshot, evaluate, screenshot, interactions) + chrome-devtools (lighthouse, perf-trace, heapsnapshot, emulate) |
| `@test-runner` | **Executes** test cases against the running stack + **triages** findings (bug/issue/enhancement: severity, root cause, repro). **REPORT-ONLY — never fixes, never opens PRs.** Writes only to `docs/QA/` ledgers. | _(no branch — diagnoses only)_ | playwright (UI/a11y/cross-browser) + chrome-devtools (lighthouse/perf-trace/memory) + create_issue (optional); runs xUnit/Karma/Playwright/axe/k6/curl via Bash |
| `@test-authenticator` | **Read-only auditor** of test quality — flags "test theater" (mock-everything, tautologies, happy-path-only, InMemory-masks-Postgres, fake isolation arms). Reports a verdict; **never edits/weakens a test.** Use after test code changes. | _(no branch — review only)_ | _none (read-only: Read/Glob/Grep/Bash)_ |
| `@integration-enforcer` | **Read-only auditor** of wiring — catches orphaned code (undispatched MediatR handlers, missing DI, unrouted Angular components, entities missing tenant query filters). Reports a verdict; **never wires it itself.** Use after implementation. | _(no branch — review only)_ | _none (read-only: Read/Glob/Grep/Bash)_ |
| `@principal-advisor` | **Read-only technical-consultant synthesizer.** Runs the /advisor v1 passes (dependency currency, ADR-drift, complexity/dead-code) + ingests existing auditor reports → ONE ranked, evidence-anchored advisory. REPORT-ONLY — never edits code/opens PRs. | _(no branch — advisory only)_ | _none (read-only: Read/Glob/Grep/Bash/WebSearch/WebFetch + microsoft-learn)_ |

| `@requirements-auditor` | **Read-only requirement→code tracer.** Given a BA module or tech-doc section, verifies each requirement against what is ACTUALLY in `src/` — code present, **wired/reachable**, and test-bound — and returns a per-requirement verdict with `file:line` evidence. Treats `STATUS.md`/`TEST-STATUS.md`/`TEST-FINDINGS.md` as unverified **claims**, never evidence; flags `CONTRADICTED` where a ledger and the code disagree (in **both** directions). REPORT-ONLY — never edits `src/`, never writes files, never opens PRs. | _(no branch — audit only)_ | _none (read-only: Read/Glob/Grep/Bash)_ |

> The last four are **auxiliary local review agents** in [`.claude/agents/review/`](.claude/agents/review/)
> (adapted from third-party MIT agent definitions, retargeted to this stack). They are read-only and
> report-only — separate from the pipeline `team/` agents above; invoke them explicitly or let them
> auto-delegate after dev/test changes.

## Skills (Slash Commands)

| Command | Mode | Description |
|---------|------|-------------|
| `/implement-all [module\|US-ID]` | Local + MCP | **Loop driver.** Picks the next pending story from `docs/BA/STATUS.md`, builds it end-to-end (BE + FE + QA in parallel), runs the full verify gate with an autonomous remediation loop, then commits + opens a PR. One story per call; rerun (or `/loop`) to continue. See below. |
| `/orchestrate` | Local + MCP | Full pipeline: BA → (FE + BE + QA in parallel via worktrees) |
| `/analyze-module {name}` | Local + MCP | Generate user stories for a specific module |
| `/research-story US-{ID}` | Local + MCP | **Feasibility gate (RPI-style).** Read-only: reads ONE story + codebase + vault and writes `docs/DEV/research/US-{ID}.md` with a GO / GO-WITH-CONDITIONS / NO-GO verdict. Run before implementing a large/risky/unclear story. |
| `/implement-story US-{ID}` | Local + MCP | Implement ONE specific story end-to-end (manual single-shot; does NOT touch STATUS.md) |
| `/test-all [module\|US-ID]` | Local + MCP | **Test loop driver — REPORT-ONLY.** Executes the next untested story's TCs via `@test-runner` and logs findings to `docs/QA/TEST-FINDINGS.md`. **Never fixes, never opens PRs.** One story per call. See below. |
| `/test-us US-{ID}` | Local + MCP | Execute the test cases for ONE specific story (manual single-shot; **REPORT-ONLY**; does NOT touch TEST-STATUS.md). |
| `/fix-finding {BUG-ID\|ISSUE-ID}` | Local + MCP | **Finding-driven fix driver.** Fixes ONE finding end-to-end on a `fix/{ID}` branch + PR, with a regression TC and the three audit gates. Edits `src/`; **does not touch the ledgers** — run `/verify-fix` after merge. |
| `/verify-fix {BUG-ID\|ISSUE-ID}` | Local + MCP | **Fix close-out.** Re-runs the finding's TCs post-merge, flips `TEST-STATUS.md`, marks the finding RESOLVED with the PR#. The only skill authorized to close a finding; writes only to `docs/QA/`. |
| `/security-audit [scope]` | Local + MCP | **HRM security gate.** Reviews a diff (branch/US-ID/path) against this platform's threat model — tenant isolation, authz, injection, secrets, PII — and writes `docs/Architecture/security-reviews/{scope}.md` with severity-by-exploitability findings + fixes. Read-only; run before opening a PR. `--deep` fans out parallel reviewers. |
| `/debug-ui {symptom\|URL}` | Local + MCP (Playwright) | Debug the running UI in a real browser — console + network + DOM diagnosis via `@browser-debugger` |
| `/design-review [URL\|--diff]` | Local + MCP (Playwright) | **Visual + UX audit — REPORT-ONLY.** Grades the *rendered* UI (WCAG, typography, spacing, AI-slop) into `docs/Design/design-reports/` with screenshots. Never edits code. |
| `/fault-diagnosis` | Local | **Root-cause-before-fix discipline.** 4-phase method (Serilog by `RequestId` → reproduce → hypothesise → fix the source), backward stack tracing, flaky-test bisection. Respects the report-only boundary. |
| `/error-recovery` | Local | **Stuck-loop breaker.** Failure counter + 2/3/4-attempt escalation (Yellow→Orange→Red), "fix the code not the test," rollback-to-known-good. Governs each attempt *inside* the `/implement-all` 3-attempt remediation cap; pairs with `/fault-diagnosis`. |
| `/retro [--since]` | Local | **Engineering retrospective.** Turns git history + PRs + ledger deltas into trends vs the last retro and 3-5 owned actions, in `docs/vault/retros/`. Also runs a **skill-friction** pass and a **setup-drift** pass (are the instructions still true?). Report-only; never weakens a guard rail. |
| `/advisor [--radar\|--adr\|--deadcode\|--module]` | Local | **Technical advisory — REPORT-ONLY.** Dependency currency, ADR-drift, complexity/dead-code → one ranked advisory in `docs/Architecture/advisory-reports/`. Never edits `src/`, deletes, or bumps deps. |
| `/gap-analysis [module\|--nfr\|--reverse\|--arch\|--rollup]` | Local | **Implemented-vs-documented tracing — REPORT-ONLY.** Traces every documented requirement to real code; passes only with code **+ wired + test-bound**, so a strong backend behind a broken FE contract is `PARTIAL`. **Never corrects a false ledger line — it reports the contradiction.** |
| `/campaign {name}` | Local + MCP | **Batch driver for a large, homogeneous, mechanical backlog.** Phase 1 is a **mandatory survey**: >20% non-mechanical **stops the campaign** (this is how BUG-310 shipped wrong code). Then pilot the smallest module, then one PR per module batch. Parks decision-required items; never closes a finding. |
| `/pr-pipeline` | Local + MCP | **Autonomous commit → push → PR → merge, and the gate that bounds it.** Merge only on a green verify gate with no CRIT/HIGH from the three audit agents, and never for a diff touching EF migrations, auth/JWT, tenant isolation, or CI/hook/settings config. Encodes Engineering-Discipline rule #7. |
| `/auto-heal` | Local | **Living-plan self-healing.** On any `OUT-OF-LANE:` flag: files it to `TEST-FINDINGS.md`, folds it into the live `GAP-CLOSURE-QUEUE.md`, re-sorts priority (severity × blast-radius × unblocks-others). Encodes Engineering-Discipline rule #6. Never bypasses report-only or the decision-gate. |
| `/github-pipeline {module}` | GitHub Actions | Trigger remote pipeline (needs API credits) |

> **Setup history — plugins, vendored skills, and the traps already hit.** Why `dotnet-skills` was
> vendored rather than installed, why `skillOverrides` was dead config, the
> `enabledPlugins`-is-not-an-installer trap this repo fell into twice, and the per-plugin overlap
> notes all live in [docs/DEV/claude-setup-history.md](docs/DEV/claude-setup-history.md).
> Read it before changing plugins, marketplaces, or the vendored skill set;
> `/retro`'s setup-drift pass is what rechecks those claims on a cadence.
### `/implement-all` — autonomous story loop

Source of truth: [.claude/skills/implement-all.md](.claude/skills/implement-all.md). Per story it:

1. Picks the first `[ ]` story in `docs/BA/STATUS.md` (scoped by module/ID arg, else priority order), marks it `[~]`, and cuts `feature/US-{MODULE}-{NNN}` from fresh `main`.
2. Runs `@backend-dev` (incl. DB/EF/migrations), `@frontend-dev`, and `@qa-engineer` **in parallel** on non-overlapping paths; sub-agents do **not** commit.
3. **Verify gate:** `dotnet build` → `scripts/run-backend-tests.sh` (never raw `dotnet test` — ISSUE-312) → `npm run build` → `ng test` (headless). Any failure enters the **remediation loop** — up to 3 attempts that hand the verbatim errors to the owning dev agent and re-run the whole gate. It may **never** weaken/skip a test to go green; if it can't fix cleanly in 3 attempts it reverts the story to `[ ]` and stops without a PR.
4. On green: commits `feat(US-XXX)`, pushes, opens a PR, flips STATUS.md `[~]`→`[x]` on `main`.

Run continuously with `/loop /implement-all [scope]` — it re-fires until the scope reports "all done." Requires a **clean working tree** on its own worktree/branch (rule #8). PRs are **opened and merged autonomously** when they clear the merge gate in [`/pr-pipeline`](.claude/skills/pr-pipeline.md); anything touching migrations, auth, tenant isolation or CI/hook config is left open for you, as is any PR with a CRIT/HIGH from the audit agents. Expect to review held PRs and parked `DECISION` findings, not a stack of everything.

### `/test-all` — autonomous test-execution loop (REPORT-ONLY)

Source of truth: [.claude/skills/test-all.md](.claude/skills/test-all.md). The testing counterpart to
`/implement-all`. **Hard policy: it identifies and documents defects but NEVER fixes them.** There is
**no remediation loop** — a failing test produces a *finding*, not a fix attempt.

Per story: picks the first `[ ]` in [docs/QA/TEST-STATUS.md](docs/QA/TEST-STATUS.md) → `@test-runner`
executes every bound TC → records each verdict and appends every defect to
[docs/QA/TEST-FINDINGS.md](docs/QA/TEST-FINDINGS.md) with the full schema → flips TEST-STATUS.md
(`[x]` clean · `[!]` findings · `[b]` blocked).

`@test-runner` writes **only** to `docs/QA/` — never edits `src/`, never weakens a test, never opens a
PR. Because nothing is auto-fixed, `/loop /test-all [scope]` is **safe unattended**; the worst case is a
longer ledger to triage. Those findings are the input to a **separate, human-decided** fix cycle.


## Automation Hooks

Full rationale, override variables and provenance: [docs/DEV/hooks.md](docs/DEV/hooks.md).
Every guard **fails open** and has a documented `CLAUDE_DISABLE_*` override, so a deliberate
exception is one env var away and a silent bypass is not.

| Hook | Trigger | Enforces |
|------|---------|----------|
| `secret-guard` | `PreToolUse` Write\|Edit | **Denies** a write whose pending content holds a hardcoded secret. Critical Rule #6. |
| `test-integrity-guard` | `PreToolUse` Write\|Edit | **Denies** skip/focus markers or removed test cases. "Never weaken a test to go green." |
| `config-protection-guard` | `PreToolUse` Write\|Edit | **Denies** edits that weaken a lint/format config to fake a green gate. |
| `freeze-guard` | `PreToolUse` Write\|Edit | **Denies** edits outside an armed directory fence. Dormant until armed. |
| `antipattern-advisor` | `PreToolUse` Write\|Edit | *Advisory.* Flags four .NET smells on `*.cs` writes. Never denies. |
| `careful-guard` | `PreToolUse` Bash | Forces a prompt on irreversible commands (`rm -r`, `DROP`, `push --force`, `reset --hard`). |
| `no-verify-guard` | `PreToolUse` Bash | **Denies** `--no-verify` and `core.hooksPath` overrides that skip git hooks. |
| `post-user-story-commit` | story files committed | Notifies dev + QA agents to start. |
| `post-dev-commit` | FE/BE code committed | Notifies QA to review test cases. |
| `vault-compliance-advisor` | `SubagentStop` | *Advisory.* Nudges when a writing agent changed ≥3 files but wrote nothing shared to `docs/vault/`. |
| sound notifications | `Stop`, `Notification`, `PermissionRequest`, `SubagentStop` | Audible cue when a long run finishes or needs you. |

## Pipeline Flow (Local + MCP)

`docs/` → `@business-analyst` (writes `docs/BA/`, opens an epic issue per module) → **Stage 2 in
parallel via git worktrees**: `@frontend-dev` + `@backend-dev` + `@qa-engineer`, each on its own
branch and PR → a GitHub integration-review issue. Every stage pushes via GitHub MCP, never manual git.

## Branch Strategy

Cut from `main`: `feature/user-stories-{module}` (BA) · `feature/frontend-{module}` ·
`feature/backend-{module}` · `feature/qa-{module}` (each in its own worktree) ·
`feature/US-{MODULE}-{NNN}` (`/implement-all`) · `fix/{BUG-ID|ISSUE-ID}` (`/fix-finding`).

## Directory Structure

The full annotated tree lives in [docs/DEV/repo-map.md](docs/DEV/repo-map.md). The parts that matter every session: `.mcp.json` (MCP servers) and `CLAUDE.md` at the repo root; `.claude/{agents,skills,rules,hooks,agent-memory}/`; `docs/{Architecture,BA,QA,DEV,Frontend,Design,vault}/`; `src/{frontend,backend}/`; and `local-dev/ · ops/ · perf/` which are operational config, NOT docs, and stay top-level.

## Shared Memory (Obsidian Vault)

All agents share a persistent markdown knowledge base at `docs/vault/`. Agents read/write the `.md` files
directly. Start at [docs/vault/Home.md](docs/vault/Home.md) and follow conventions in
[docs/vault/README.md](docs/vault/README.md).

> **Open the Obsidian vault at `docs/`, not `docs/vault/`** (config committed in `docs/.obsidian/`). Rooted at
> `docs/vault/` it is a 34-note island; rooted at `docs/` the BA stories, QA ledgers, ADRs and architecture
> are one graph — `[[US-PLT-005]]` resolves to the story and `[[TEST-FINDINGS-RESOLVED#BUG-292]]` to the finding.
> **Wikilinks resolve by note NAME, never by path** — `[[authentication-sso]]`, never
> `[[../modules/authentication-sso]]`. That single mistake produced 21 of the 38 broken links found on
> 2026-08-22.

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
| **Built-in agent memory** (`.claude/agent-memory/{agent}/`) | Auto-loaded each run via `memory: project` in agent frontmatter. Scoped to one agent, but **tracked in git since 2026-08-22**. | An agent's own operational notes ("tried X, it failed", recurring gotchas) it wants auto-recalled next run. |
| **Claude's own auto-memory** (`~/.claude/projects/…/memory/`) | Outside the repo, per-human, **never tracked**. Not a vault store. | One person's cross-session recall. **Never wikilink these from the vault** — write `` `memory:name` ``; four such dead links existed before 2026-08-22. |

Rule of thumb: if it's worth sharing, it goes in the **vault**; if it's just one agent's working memory, the
built-in store is fine. Never duplicate the same fact into both. Secrets/logs go in neither.

## Critical Rules
1. **Tenant isolation is non-negotiable** — every query, cache key, and API call must be tenant-scoped
2. **IEEE standards** — user stories follow IEEE 830, test cases follow IEEE 829
3. **Parallel execution** — dev agents and QA agent run simultaneously via git worktrees
4. **Traceability** — every test case must link back to a user story and acceptance criteria
5. **MCP-first** — prefer GitHub MCP tools over manual git commands for branch/PR/issue operations
6. **Secrets in .env only** — never hardcode tokens, always use `${ENV_VAR}` references

## Module Priority

1. Authentication & Authorization · 2. Core HR · 3. Leave · 4. Attendance · 5. Recruitment ·
6. Payroll · 7. Performance · 8. Admin Console · 9. Onboarding/Offboarding · 10. Training &
Benefits · 11. Reports & Analytics · 12. Notifications & Audit
---

# Application Development

> The sections above describe the **agent-orchestration meta-system**. The application itself — how to
> build, run and test it, and how its architecture fits together — lives in **path-scoped rules** under
> [.claude/rules/](.claude/rules/), which load automatically when Claude opens a matching file:
>
> | Rule | Loads when you touch | Covers |
> |---|---|---|
> | [backend.md](.claude/rules/backend.md) | `src/backend/**` | commands, `HRM.Tests` + the ISSUE-312 wrapper, EF/migrations, Clean Architecture + CQRS, the three tenant-isolation layers, Serilog/Hangfire/JWT |
> | [frontend.md](.claude/rules/frontend.md) | `src/frontend/**` | commands incl. `npm run lint`, standalone Angular 20 layout, generated-types rule, a11y debt |
> | [ledgers.md](.claude/rules/ledgers.md) | `docs/QA/**`, `docs/BA/**` | IEEE 829/830, traceability, finding schema, report-only boundary |
> | [vault.md](.claude/rules/vault.md) | `docs/vault/**` | wikilink rules, the three memory stores |
>
> **Why split:** CLAUDE.md loads in full every session, and the official guidance targets **under 200
> lines** — longer files still load but adherence drops. Path-scoped rules keep module detail out of
> every session while guaranteeing it is present the moment it is relevant. Rules are *guidance*, not
> enforcement: for guaranteed behaviour use hooks or permissions, both of which this repo already has.

## Local Configuration (required to run)
`appsettings.json` ships with **blank secrets** — the app will not start until these are set, ideally via .NET user-secrets (`UserSecretsId` is already in `HRM.Api.csproj`), not by editing the committed file:
- `ConnectionStrings:DefaultConnection` — PostgreSQL (`Password` is empty in the template)
- `Jwt:PrivateKey` — signing key for JWT validation
- A running **PostgreSQL** instance (also backs Hangfire job storage)

Run **`scripts/doctor.sh`** after any toolchain or plugin change. It checks the two tiers
separately — REQUIRED (exit 1: cannot build) and CAPABILITY (exit 2: builds fine, but a
capability these instructions promise is silently absent). The second tier exists because
`csharp-lsp`/`typescript-lsp` ship no language server: both were documented as available
and were dead on PATH for 12 days with no build, test, or agent ever noticing.

## Traceability convention
Code, user stories (`docs/BA/`, IEEE 830), and test cases (`docs/QA/`, IEEE 829) are cross-referenced by ID — e.g. `US-AUTH-007` appears in both `TenantService` comments and `docs/QA/authentication/`. Preserve these references when modifying related code.
