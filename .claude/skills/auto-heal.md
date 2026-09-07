---
name: auto-heal
description: Living-plan self-healing protocol. When work surfaces an out-of-lane discovery (a new bug/gap, an adjacent-module dependency, a broken sibling test, a missing endpoint, a licensing/infra snag), capture it as a finding, fold it into the completion plan, and re-sort the priority order — so nothing is silently dropped and the plan stays in sync with reality. Use whenever a sub-agent reports an OUT-OF-LANE block, a fix reveals adjacent work, or on demand to reconcile plan ⇄ ledger ⇄ code.
user_invocable: true
---

# Auto-Heal (living-plan self-healing protocol)

> The orchestrator's counterpart to every agent's **out-of-lane discovery contract**. Sub-agents *flag*
> out-of-lane discoveries in a structured block; **this skill is how the main loop *heals* them** — files the
> finding, refreshes the plan, and re-prioritizes — instead of letting them evaporate in a transcript.
> The completion plan is a **living document**: it is expected to change every time reality does.

## Prime directive

```
NEVER SILENTLY DROP AN OUT-OF-LANE DISCOVERY.
Every gap surfaced by any run is FILED, FOLDED INTO THE PLAN, and RE-PRIORITIZED — automatically.
```

A campaign that only fixes what it set out to fix, while quietly discovering ten new things it ignores, is
lying to itself. Auto-heal makes discovery → tracking → re-prioritization a reflex, not a favor.

## ⏱ WHEN: immediately — the same turn the discovery surfaces

```
FILE IT WHEN YOU FIND IT. NEVER BATCH OUT-OF-LANE FINDINGS TO THE END OF A RUN.
```

The prime directive above says *what*; this says *when*, because the omission was the whole failure mode.
The moment a sub-agent returns an `OUT-OF-LANE:` block — or you notice one yourself — write it to
`docs/QA/TEST-FINDINGS.md` and fold it into `docs/QA/plans/GAP-CLOSURE-QUEUE.md` **before starting the next
queue item**. Not after the PR is opened. Not at the end of the tier.

**Why this is a hard rule and not a preference.** On 2026-09-07, eleven out-of-lane findings from one
T1/T3 batch sat in transcripts, reported in prose and written to neither ledger. One of them was
`ISSUE-501` — **HIGH**: EPF/ETF statutory contribution rates silently rounded by a missing scale rule, a
legal-obligation money path, and a copy of a defect that had *already been fixed one field over in the same
batch*. It was written down only because the user asked whether the out-of-lane items had been logged.
The loop did not catch it; a human did. Batching is precisely how a HIGH becomes invisible: each individual
deferral looks harmless, and the aggregate is a lost severity.

**Practical notes:**

- **The ledger-lock rule constrains WHERE a filing lands, never WHEN.** If a ledger PR is already open,
  append to that open branch — do not defer the filing until it merges. "I'll file it once the lock frees"
  is the same batching failure wearing a process justification.
- **A CRIT/HIGH out-of-lane finding re-sorts the queue on the spot**, even mid-item, and is named in that
  turn's summary. It may legitimately outrank the story you are halfway through — say so rather than
  finishing the lower-value item out of momentum.
- **Recount the summary table when you file.** `LedgerTraceabilityTests.TheSummaryTable_MatchesTheActualCounts`
  asserts it against both ledger files and will fail CI otherwise. Recount from the files; do not hand-edit
  the numbers to go green — the guard's own message says so.
- **A finding filed with only a title is half-filed.** Severity, `file:line` evidence, and the reason it was
  out of lane are what make it actionable later; without them the next reader re-derives the investigation.

## Trigger (any of)

- A sub-agent's report contains an **`OUT-OF-LANE:`** block (the structured flag every team/review agent must
  emit when it finds something outside its assigned lane — see each agent's "Out-of-lane discovery contract").
- A fix **reveals adjacent work**: the correct fix needs a change in another module/layer, or a dependency,
  migration, config, or infra decision the task didn't scope.
- A gate run surfaces **collateral**: a broken sibling test that is a stale fixture, a pre-existing red suite,
  a licensing/build snag (e.g. a NuGet that needs a paid key), an unbuilt endpoint the FE already calls.
- A verification/sweep re-classifies a finding (LIVE ⇄ STALE), invalidating the current priority order.
- **On demand**: run `/auto-heal` to reconcile plan ⇄ `TEST-FINDINGS.md` ⇄ code end-to-end.

## Roles

| Actor | Responsibility |
|---|---|
| **Sub-agent (any team/review agent)** | **FLAG, never heal.** Emit the `OUT-OF-LANE:` block; do NOT scope-creep to fix it (except a *trivial, clearly-correct, same-file* correction — and even then, note it). Stay in your lane. |
| **Orchestrator (main loop)** | **HEAL.** On any flag or trigger above, run the heal steps below. This is not optional and not deferred to "later". |

## The `OUT-OF-LANE:` flag format (what agents emit)

```
OUT-OF-LANE:
  type:        BUG | ISSUE | ENH | GAP | DEPENDENCY | INFRA | TEST-HEALTH | DECISION
  severity:    CRIT | HIGH | MED | LOW
  where:       <file:line or module/endpoint>
  what:        <one sentence: the discovered gap>
  why_oo_lane: <why it's outside this task's lane — different module/layer/decision/infra>
  suggested:   <build | remove-dead-control | fix-in-<lane> | needs-decision | needs-infra>
  blocks:      <what it blocks, if anything — e.g. "the FE-only half of BUG-243">
```

Free-form flags are still honored — the orchestrator normalizes them — but the structured block makes healing
deterministic.

## Heal steps (orchestrator runs these on every trigger)

1. **File the finding.** Append to [docs/QA/TEST-FINDINGS.md](../../docs/QA/TEST-FINDINGS.md) with the
   full schema (type · severity · status OPEN · layer · module/US/TC · title · root-cause+confidence · repro ·
   evidence · severity rationale · suggested direction), **plus a SURVEY and an AUDIT — both mandatory**
   (Engineering-Discipline rule #7). **SURVEY:** how many call sites/files/services, and the unit counted —
   one instance or a class? **AUDIT:** every claim verified against `src/` with `file:line`, in both
   directions (confirm the defect *and* the premise). An out-of-lane flag that arrives without them is
   not filed as-is: do the survey and the audit first, or file it explicitly marked
   `SURVEY: not done` / `AUDIT: not done` so the gap is visible rather than implied. Assign the next free ID by scanning **BOTH** ledger files
   (`grep -hoE 'BUG-[0-9]+|ISSUE-[0-9]+|ENH-[0-9]+' docs/QA/TEST-FINDINGS*.md | sort -t- -k2 -n | tail -1` → +1) —
   the ledger was split 2026-09-01 and scanning only the working file re-issues an archived id. Cross-link the parent finding/PR with `[[wiki-links]]`. **De-dup first, across BOTH files** — if it's the same defect as an existing finding, extend that
   one instead of minting a new ID. A recurring regression's original is usually in the archive.
2. **Fold it into the LIVE QUEUE.** Add it to [docs/QA/plans/GAP-CLOSURE-QUEUE.md](../../docs/QA/plans/GAP-CLOSURE-QUEUE.md)
   — *the loop's source of truth*, executed top-down, one item per iteration. **This is the heal target
   (decided 2026-09-01).** It used to be `COMPLETION-PLAN.md`, which the loop stopped reading around
   2026-08-22: findings were being filed into a document nothing executed, which is the silent drop this
   skill exists to prevent. `COMPLETION-PLAN.md` remains the broad living backlog, refreshed at
   `/retro` and `/gap-analysis` cadence — not per-heal. Older guidance naming it here is superseded
   under the phase/theme it belongs to (or the "loop-discovered items" section), tagged `[NEW]` with its finding ID and a one-line
   disposition (build / remove / decision / infra).
3. **Re-sort the priority order.** Recompute the execution order with:
   `priority ≈ severity × blast-radius × unblocks-others − gated`
   - **severity**: CRIT ≫ HIGH ≫ MED ≫ LOW.
   - **blast-radius**: systemic/cross-tenant/security ≫ whole-module ≫ local.
   - **unblocks-others**: an enabler that frees several downstream items (e.g. a resolver, a shared util) ranks up.
   - **gated**: anything needing a product **decision** or **infra** provisioning is parked at the **decision-gate**,
     not auto-scheduled — no matter how high its raw score.
   Update the plan's "recommended order / next" so the top of the queue reflects the new reality.
4. **Respect the gates & boundaries.** Auto-heal **files and re-prioritizes**; it does **not** auto-implement
   decision/infra-gated work, and it never weakens/skips a test to go green, never crosses the report-only
   boundary (`/test-all`, `@test-runner`). Merging its own PRs IS now authorized — bounded by the merge
   gate in [pr-pipeline](pr-pipeline.md), which holds migrations, auth, tenant-isolation and CI/hook
   diffs open for a human however green the run was.
5. **Surface what matters.** If the new finding is **CRIT/HIGH**, **changes the critical path**, or **needs a
   decision/infra**, tell the user in the turn summary (with the re-prioritization); otherwise record it and keep
   moving. Never bury a severity ≥ HIGH discovery in a commit message alone.

## What auto-heal is NOT

- Not a licence to scope-creep: agents still **stay in their lane**; healing happens at the orchestrator level,
  as *tracking + planning*, not as spontaneous cross-lane edits.
- Not a bypass of the decision-gate: gated items get **tracked and ranked**, then **wait** for the human call.
- Not a substitute for verification: a flagged "LIVE" bug is still verified before it's trusted (a stale ledger
  entry heals *down* to RESOLVED just as readily as a real one heals *in*).

## Relationship to the rest of the system

- **Feeds on** the out-of-lane contract in every `team/` + `review/` agent and the discoveries from the
  completeness sweep (integration-enforcer, contract-drift, US-AC audits).
- **Writes to** `TEST-FINDINGS.md` (the ledger — live findings only; terminal ones live in
  `TEST-FINDINGS-RESOLVED.md`) and `GAP-CLOSURE-QUEUE.md` (the live queue the loop executes).
  `COMPLETION-PLAN.md` is the broad backlog and is NOT written per-heal.
- **Complements** `/error-recovery` (stuck-loop breaker — retries), `/fault-diagnosis` (root-cause-before-fix),
  and the `/implement-all` remediation loop. Auto-heal is about *breadth* (don't lose discoveries); those are
  about *depth* (don't thrash on one fix).
- **Invoked** automatically by the loop drivers and on demand as `/auto-heal`.
