# Design Spec — `/advisor` (technical-consultant advisory role)

- **Date:** 2026-07-08
- **Status:** approved (design); pending implementation plan
- **Type:** new Claude Code skill + review agent (report-only)

## 1. Context & problem

The team wants a "technical consultant / principal-engineer" role that advises the *best* way (not
quick wins, never hiding risks) with pros/cons on tools/libraries; keeps architecture **decisions
current**; tracks **day-to-day technology updates**; surveys implementations; finds bottlenecks,
security vulnerabilities, performance opportunities, and junk/dead code; and suggests genuinely
valuable UX enhancements.

**Key finding from research (the design's foundation):** ~70% of those responsibilities already have
strong, dedicated *report-only* owners in this repo, and the dominant failure mode of "consultant"
agents is a broad **mega-agent that duplicates specialists and emits generic, un-anchored advice**.
So this role is designed as a **thin synthesis/orchestration layer** that reuses the existing
auditors and adds only the **3 capabilities nothing owns today**.

### Coverage of the 9 asks (why v1 is scoped the way it is)
| Responsibility | Status | Existing owner |
|---|---|---|
| Security vulnerabilities | covered | `/security-audit`, `secret-guard`, `.semgrep/tenant-isolation.yml`, `gitleaks.yml` |
| UX enhancements | covered | `/design-review`, `@browser-debugger` |
| Survey implementations | covered | `integration-enforcer`, `analyze-module`, vault module notes |
| Best-way pros/cons advice | partial | ADR template + `TOOLING-ADOPTION-PLAN.md` (static; nothing generates on demand) |
| Bottlenecks / perf opportunities | partial | `fault-diagnosis`, Lighthouse (reactive, not a proactive sweep) |
| Junk / dead code | partial | `integration-enforcer` catches *orphaned* only; no complexity/CRAP/analyzers wired |
| **Keep decisions current** | **gap** | ADRs exist; nothing detects stale/drifted decisions |
| **Track tech updates** | **gap** | tooling plan is a static snapshot; no live tech-radar process |
| **Complexity/dead-code signal** | **gap** | Knip / CRAP / Roslyn analyzers not wired |

## 2. Locked decisions

1. **Shape:** a thin `/advisor` orchestrator **skill** (report-only) + one new read-only
   `@principal-advisor` **synthesis agent**. NOT a mega-agent; NOT a large new suite.
2. **v1 scope:** the **3 net-new gaps** — tech-radar currency, ADR-drift, complexity/dead-code —
   plus a *light* synthesis that links out to the existing auditors for the other 6 areas.
3. **Authority:** **report-only — advise + document.** Writes the radar/advisory/proposed-ADRs;
   never edits `src/`, deletes code, bumps deps, or wires fitness tests itself. Humans / dev agents /
   `/fix-finding` act.

## 3. Goals & non-goals

**Goals (v1):** (a) a living **Tech Radar** (Adopt/Trial/Assess/Hold) kept current from real dependency
data + disciplined new-tech scouting; (b) **ADR-drift detection** — flag decisions whose *Consequences*
no longer match the code; (c) a **complexity/dead-code signal** (Knip + CRAP + Roslyn warnings) with
false-positive filtering; (d) an **evidence-anchored, ranked, candid advisory** that synthesizes the
above and links to existing auditor output.

**Non-goals (v1):** re-running or duplicating `/security-audit`, `/design-review`,
`integration-enforcer`, `test-authenticator`, `fault-diagnosis`; authoring ArchUnitNET **fitness
functions** (v1 *recommends* them; a human/`backend-dev` writes them); driving paid/GUI tools
(NDepend, ReSharper, dotTrace); any auto-application of fixes; full synthesis over all 9 areas.

## 4. Components

### 4.1 `.claude/skills/advisor.md` (orchestrator skill, report-only, user-invocable)
Parses args, dispatches `@principal-advisor` with the requested passes, then writes the documents from
the agent's returned synthesis. Usage:
```
/advisor            # full v1 (all 3 passes + synthesis)
/advisor --radar     # tech-radar / dependency-currency only
/advisor --adr       # ADR-drift only
/advisor --deadcode  # complexity/dead-code only
/advisor --module CHR  # scope the dead-code/complexity + advisory to one module
```

### 4.2 `.claude/agents/review/principal-advisor.md` (new read-only synthesis agent)
Follows the repo's review-agent convention (execution contract → phases → output template →
out-of-lane block). Tools: `Read, Glob, Grep, Bash, WebSearch, WebFetch, mcp__microsoft-learn__*`.
**No Write/Edit** — it *returns* a compact structured advisory; the skill writes the artifacts (this
keeps the main-loop context lean and matches `/design-review` → `@browser-debugger`). `model: claude-opus-4-8`,
`maxTurns: 40`, `memory: project`.

## 5. The 3 passes (what each runs → produces)

**A. Tech-radar / currency.** Drives `dotnet list package --outdated`, `--vulnerable`, `--deprecated`
(`--format json`); `npm outdated --json`; `npm audit --json`. Cross-checks against
`TOOLING-ADOPTION-PLAN.md` (adopted vs planned vs landed). Scouts new/relevant tech via `WebSearch` +
the **microsoft-learn MCP** — *disciplined*: new tech defaults to **Assess/Hold**, and every
Adopt/Trial move must state fit-for-this-stack + migration cost (guards against radar hype inflation).
→ updates `docs/Architecture/radar/tech-radar.md`.

**B. ADR-drift.** Reads `docs/vault/decisions/*.md`. For each ADR, verifies its **Decision /
Consequences** still hold in the code/config (examples: governance ADR says "RLS planned" → is
`appsettings*.json` `Rls:Enabled` still `false`? "self-hosted GlitchTip wired" → is the Sentry DSN set?
"Gitleaks advisory→hard-gate after secret rotation" → has `--exit-code` flipped?). → flags
**drifted / stale / superseded** decisions + drafts proposed ADR updates for human acceptance.

**C. Complexity / dead-code.** Drives **Knip** (`npx knip` — Angular/TS unused files/exports/deps),
Roslyn `IDE0051`-class **build warnings** (`dotnet build`), and **CRAP** via the
`dotnet-skills:crap-analysis` skill (Coverlet OpenCover + ReportGenerator). **Cross-checks every
dead-code candidate against `@integration-enforcer`'s wiring map** to filter DI/MediatR/EF/route
false positives. → dead-code **candidates (human-confirm only)** + complexity/risk hotspots.
*Graceful degradation:* if Knip/CRAP/analyzers aren't wired yet, run what's present and **flag the gap**
(e.g. "Roslyn analyzers absent from `Directory.Build.props` → Wave 2 of the tooling plan"), never fail.

## 6. Synthesis & the honesty contract (the differentiator)

`@principal-advisor` merges the 3 passes, optionally ingests existing reports (`docs/Architecture/security-reviews/`,
`docs/Design/design-reports/`, `TEST-FINDINGS.md`) to **link** rather than re-run them, dedupes, and ranks by
**severity × effort × blast-radius**. Non-negotiable rules (inherit CLAUDE.md Advisor Stance):
1. **Evidence-or-it-doesn't-exist** — every finding cites a tool output / `file:line` / CVE / CRAP
   number / drifted-ADR reference. Un-anchored "consider improving X" is banned.
2. **Confidence rating** on every non-obvious claim.
3. **Cost-of-inaction** stated for every recommendation (surfaces the hidden risk).
4. **Two clearly-separated sections:** "What the tools measured" (facts) vs "What I recommend" (rated
   judgment) — the reader must always tell which is which.
5. **Adversarial self-pass** — drop any finding that "could have come from a generic blog," any
   trend-chasing recommendation not justified for this stack, and any version-blind claim (verify
   against microsoft-learn / official docs; be Angular-20 / .NET-10 aware).
6. **No ledger spam** — cap findings per run; "would a senior engineer bother?" filter.

## 7. Outputs (the "document" mandate)
- `docs/Architecture/advisory-reports/{scope}-{YYYY-MM-DD}.md` — the ranked, evidence-anchored advisory.
- `docs/Architecture/radar/tech-radar.md` — living, versioned Adopt/Trial/Assess/Hold radar.
- Proposed ADR stubs/updates in `docs/vault/decisions/` (drift + new-decision candidates) for human
  acceptance.
- Actionable items fold into **`/auto-heal` + `TEST-FINDINGS.md`** (the existing seam) — the advisor
  never edits `src/`, deletes code, or bumps deps.

## 8. Tools: DRIVE vs RECOMMEND
- **DRIVE (agent runs + parses):** `dotnet list package --outdated/--vulnerable/--deprecated`,
  `npm outdated`/`npm audit`, **Knip**, Roslyn build warnings, **CRAP** (via `crap-analysis`),
  and (linked, not re-run) k6 / Lighthouse via existing agents.
- **RECOMMEND only (paid/GUI/config):** NDepend, ReSharper CLI, BenchmarkDotNet (advisor picks
  targets; humans author), Renovate (advisor reviews its PRs), ArchUnitNET fitness functions (advisor
  recommends; humans author).

## 9. Relationship to existing tooling
- **Complements, never duplicates** the report-only fleet — it ingests/links their output.
- **Feeds `/auto-heal`** via the standard `OUT-OF-LANE:` block for actionable findings.
- **vs `/retro`:** retro is backward-looking (what shipped); advisor is forward-looking (what to
  adopt / fix / decide).
- **vs `/plan-audit`-style skills:** advisor audits *technical* health + decisions; plan/ledger
  hygiene stays with `auto-heal`/`retro`.

## 10. Success criteria
- A `/advisor` run produces a ranked advisory where **every finding is evidence-anchored** (0 generic
  items survive the adversarial pass).
- The tech-radar reflects **real** `dotnet list package`/`npm outdated` data, not guesses.
- ADR-drift correctly flags at least the known live drift (e.g. RLS-planned-vs-not-built,
  Gitleaks-still-advisory) with `file:line`/config evidence.
- Dead-code output is **candidates only**, cross-checked against wiring — zero auto-deletions.
- Degrades gracefully when Knip/CRAP/analyzers are unwired (flags the gap, still runs).

## 11. Out of scope / future (v2+)
Full synthesis across all 9 areas; authoring the ArchUnitNET fitness-function suite (esp. the
tenant-query-filter invariant tied to Critical Rule #1); scheduled/periodic runs; paid-tool drivers.
