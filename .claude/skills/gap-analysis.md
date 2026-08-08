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
(executes tests against a running stack), and **not** `@integration-enforcer` (wiring of one change).
It is requirement-to-code tracing across the whole product.

## The premise you must not forget

`docs/BA/STATUS.md` claims **124 of 125 stories done**. That ledger has been measured wrong in **both**
directions. `docs/QA/TEST-STATUS.md` and `TEST-FINDINGS.md` inherit the same disease. So:

> **Ledgers are claims to be tested, never evidence.** Every verdict must be anchored to `src/`. Where
> a ledger claim and the code disagree, that contradiction is itself a first-class finding
> (`CONTRADICTED`) — usually more valuable than the gap underneath it, because the false "done" is
> what prevents anyone from fixing it.

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

Story-level granularity is precisely what produced the current wrong ledger. The Must-Have tier is
where a wrong verdict is expensive, so it gets AC-level scrutiny; the Should/Could tail does not earn
that cost.

## Evidence bar

`IMPLEMENTED` requires **code exists** + **wired/reachable** + **a bound test exists**. Test existence
is *recorded, not executed* — running tests is `/test-all`'s job. Failing any leg yields `PARTIAL`
with the failing leg named. Full taxonomy and the stack-specific wiring checklist live in
[`.claude/agents/review/requirements-auditor.md`](../agents/review/requirements-auditor.md).

**Leg 2 includes the FE/BE contract.** The pilot found the single highest-value gap class in this
codebase is an Angular layer coded against a response shape the API cannot emit, with green Karma
specs mocking the wrong shape. A strong backend does not make the AC implemented.

## The five passes

| Pass | Question | Scope | Agents |
|---|---|---|---:|
| **A — Functional forward** | Do the stories' ACs exist in code? | 125 stories / 13 BA modules | 13 |
| **B — Doc→story coverage** | Does the tech doc promise modules/features no story covers? | §3.1, §5.1, §5.2, §11.1–11.14 vs `BA/INDEX.md` | 1 |
| **C — NFR** | Are the §6 non-functional requirements satisfied? | §6.1–6.12 | 1 |
| **D — Reverse** | Is there shipped code no document describes? | 36 BE features + 15 FE features | 1 |
| **E — Architecture conformance** | Does the build match §8/§9/§10? | request pipeline, layering, tenancy touchpoints, layout | 1 |
| **F — Synthesis** | What is the ranked register? | orchestrator, no agent | — |

Pass A carries the bulk. Passes B–E are single-agent and run concurrently with it.

## Execution protocol

1. **Check the working tree is exclusively yours.** `git reflog --date=iso -8` and `ps aux | grep claude`.
   If another session is mutating this tree, **stop** — a concurrent `checkout`/`pull` will silently
   destroy untracked pass outputs mid-run. (This happened during the pilot; see the plan's incident log.)
   Commit pass outputs as they land rather than holding them untracked.
2. **Pilot first — do not fan out blind.** Run Pass A against **one** module and read the output
   yourself. Spot-check three verdicts by opening the cited files. If the agent is rubber-stamping
   (IMPLEMENTED with vague evidence) or manufacturing gaps (MISSING for code that plainly exists
   under another name), fix the prompt before spending the other twelve agents. A fan-out of
   unvalidated auditors just produces a second wrong ledger, faster.
3. **Fan out** the remaining Pass A modules in waves, plus B/C/D/E concurrently. Independent scopes
   only.
4. **Persist** each pass's returned text verbatim to
   `docs/Architecture/gap-analysis/pass-{a-module|b|c|d|e}.md`.
5. **Synthesize (Pass F)** into `docs/Architecture/gap-analysis/GAP-REGISTER.md`:
   - Stable ID `GAP-001…` — never renumber; append only on re-runs.
   - Columns: ID · requirement source · MoSCoW · verdict · severity · evidence · closing move · size.
   - Sort: **Must+CONTRADICTED → Must+MISSING → Must+PARTIAL → Should+… → Could+…**, tie-broken by
     blast radius (tenant isolation and auth outrank cosmetic gaps at every tier).
   - Headline rollup: claimed-done vs verified-done per MoSCoW tier.
6. **Do not silently cap.** If a pass sampled rather than covered, say what was left out and why.
7. **Hand off, do not fix.** Fold actionable gaps into
   [`docs/QA/plans/COMPLETION-PLAN.md`](../../docs/QA/plans/COMPLETION-PLAN.md) and the session TODO
   via [`/auto-heal`](auto-heal.md). Genuine defects also get filed to `docs/QA/TEST-FINDINGS.md`
   with the standard schema. Fixing is a separate, human-decided cycle (`/fix-finding`,
   `/implement-story`).

## Hard boundaries

- **Never edits `src/`.** Not one line, not "while I was in there".
- **Never edits `docs/BA/STATUS.md` or `docs/QA/TEST-STATUS.md`.** Correcting a false ledger line is a
  real change with real consequences; this skill *reports* the contradiction and lets the human decide.
  (`/verify-fix` is the only skill authorized to close a finding.)
- **Never runs a mutating git command.** No `checkout`, `stash`, `clean`, `pull`, `commit`, `restore`
  from an auditor — the tree may be shared.
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
