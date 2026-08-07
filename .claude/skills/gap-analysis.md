---
name: gap-analysis
description: "Implemented-vs-documented gap analysis (REPORT-ONLY). Traces every documented requirement — BA user stories, tech-doc §5/§11 functional, §6 NFR, §8/§9/§10 architecture — to actual code in src/, and reports what is really built vs what the ledgers claim, bucketed by MoSCoW (Must/Should/Could). Produces a numbered GAP register that feeds the COMPLETION-PLAN. Never edits src/, never fixes anything, never opens PRs."
user_invocable: true
---

# Gap Analysis (report-only)

Answers one question honestly: **what does the documentation promise that the code does not deliver —
and what does the code deliver that no document describes?** Output is a ranked, MoSCoW-bucketed gap
register with file:line evidence for every claim.

This is **not** `/advisor` (tech health, dependency currency, ADR drift), **not** `/test-all`
(executes tests against a running stack), and **not** `/integration-enforcer` (wiring of one change).
It is requirement-to-code tracing across the whole product.

## The premise you must not forget

`docs/BA/STATUS.md` claims **124 of 125 stories done**. That ledger has been measured wrong in **both**
directions. `docs/QA/TEST-STATUS.md` and `TEST-FINDINGS.md` inherit the same disease. So:

> **Ledgers are claims to be tested, never evidence.** Every verdict in this skill's output must be
> anchored to `src/`. Where a ledger claim and the code disagree, that contradiction is itself a
> first-class finding (`CONTRADICTED`) — usually more valuable than the underlying gap, because the
> false "done" is what prevents anyone from fixing it.

## Invocation

```
/gap-analysis                 # full run — all five passes
/gap-analysis {module}        # Pass A for one BA module only (e.g. leave-management)
/gap-analysis --nfr           # Pass C only — tech doc §6 non-functional requirements
/gap-analysis --reverse       # Pass D only — shipped code with no story/doc
/gap-analysis --arch          # Pass E only — architecture conformance §8/§9/§10
/gap-analysis --rollup        # re-synthesize the register from existing pass outputs, no new audits
```

## Depth rule

| Requirement class | Granularity |
|---|---|
| `priority: Must Have` stories | **Per acceptance criterion** — one verdict per AC |
| `priority: Should Have` / `Could Have` stories | **Per story** — one verdict per story |
| Tech-doc NFR / architecture bullets | Per documented requirement bullet |

Rationale: story-level granularity is precisely what produced the current wrong ledger. The Must-Have
tier is where a wrong verdict is expensive, so it gets AC-level scrutiny; the Should/Could tail does
not earn that cost.

## Evidence bar

A requirement is `IMPLEMENTED` only if **code exists** + **it is wired/reachable** + **a test is bound
to it**. Test existence is *recorded, not executed* — running tests is `/test-all`'s job. Failing any
one of the three yields `PARTIAL` with the specific failure named. Full taxonomy and the stack-specific
wiring checklist live in the agent definition:
[`.claude/agents/review/requirements-auditor.md`](../agents/review/requirements-auditor.md).

## The five passes

| Pass | Question | Scope | Agents |
|---|---|---|---|
| **A — Functional forward** | Do the stories' ACs exist in code? | 125 US files across 13 BA modules | one `@requirements-auditor` per module |
| **B — Doc→story coverage** | Does the tech doc promise modules/features no story covers? | tech doc §3.1, §5.1, §5.2, §11.1–11.14 vs `docs/BA/INDEX.md` | 1 |
| **C — NFR** | Are the §6 non-functional requirements actually satisfied? | tech doc §6.1–6.12 | 1 |
| **D — Reverse** | Is there shipped code no document describes? | 36 backend feature folders + 15 frontend features vs stories/doc | 1 |
| **E — Architecture conformance** | Does the built structure match §8/§9/§10? | request pipeline, Clean Architecture layering, tenancy touchpoints, folder layout | 1 |
| **F — Synthesis** | What is the ranked gap register? | orchestrator, no agent | — |

Pass A carries the bulk. Passes B–E are single-agent and run concurrently with it.

## Execution protocol

1. **Pilot first — do not fan out blind.** Run Pass A against **one** module and read the output
   yourself. Spot-check two or three verdicts by opening the cited files. If the agent is
   rubber-stamping (IMPLEMENTED with vague evidence) or manufacturing gaps (MISSING for code that
   plainly exists under another name), fix the prompt before spending the other twelve agents. A
   fan-out of unvalidated auditors just produces a second wrong ledger faster.
2. **Fan out** the remaining Pass A modules in waves, plus B/C/D/E concurrently. Independent scopes
   only — never parallelize two agents that write the same output file (they write nothing; the
   orchestrator persists).
3. **Persist** each pass's returned text verbatim to
   `docs/Architecture/gap-analysis/pass-{a-module|b|c|d|e}.md`.
4. **Synthesize (Pass F)** into `docs/Architecture/gap-analysis/GAP-REGISTER.md`:
   - Assign each gap a stable ID `GAP-001…` (never renumber; append only on re-runs).
   - Columns: ID · requirement source · MoSCoW · verdict · severity · evidence · closing move · size.
   - Sort: **Must+CONTRADICTED → Must+MISSING → Must+PARTIAL → Should+… → Could+…**, tie-broken by
     blast radius (tenant isolation and auth outrank cosmetic gaps at every tier).
   - Roll up a headline table: claimed-done vs verified-done per MoSCoW tier.
5. **Do not silently cap.** If a pass sampled rather than covered, say what was left out and why.
   Silent truncation reads as "we checked everything" when we did not.
6. **Hand off, do not fix.** Fold actionable gaps into
   [`docs/QA/plans/COMPLETION-PLAN.md`](../../docs/QA/plans/COMPLETION-PLAN.md) and the live session
   TODO via [`/auto-heal`](auto-heal.md). Findings that are genuine defects also get filed to
   `docs/QA/TEST-FINDINGS.md` with the standard schema. Fixing is a separate, human-decided cycle
   (`/fix-finding`, `/implement-story`).

## Hard boundaries

- **Never edits `src/`.** Not one line, not "while I was in there".
- **Never edits `docs/BA/STATUS.md` or `docs/QA/TEST-STATUS.md`.** Correcting a false ledger line is a
  real change with real consequences; this skill *reports* the contradiction and lets the human decide.
  (`/verify-fix` is the only skill authorized to close findings.)
- **Never opens a PR.**
- **Never lets a ledger promote a verdict.** If the only evidence for "done" is that a doc says done,
  the verdict is MISSING or UNVERIFIABLE.

## Output

```
docs/Architecture/gap-analysis/
├── GAP-REGISTER.md          # the deliverable — ranked, MoSCoW-bucketed, evidence-anchored
├── GAP-ANALYSIS-PLAN.md     # execution plan + live status of each pass
├── pass-a-{module}.md       # 13 module audits (raw agent output)
├── pass-b-doc-coverage.md
├── pass-c-nfr.md
├── pass-d-reverse.md
└── pass-e-architecture.md
```
