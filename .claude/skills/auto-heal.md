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

1. **File the finding.** Append to [test-cases/TEST-FINDINGS.md](../../test-cases/TEST-FINDINGS.md) with the
   full schema (type · severity · status OPEN · layer · module/US/TC · title · root-cause+confidence · repro ·
   evidence · severity rationale · suggested direction). Assign the next free ID (`grep -oE 'BUG-[0-9]+|ISSUE-[0-9]+'`
   → max+1). Cross-link the parent finding/PR with `[[wiki-links]]`. **De-dup first** — if it's the same defect
   as an existing finding, extend that one instead of minting a new ID.
2. **Fold it into the plan.** Add it to [test-cases/COMPLETION-PLAN-*.md](../../test-cases/) under the phase/theme
   it belongs to (or the "loop-discovered items" section), tagged `[NEW]` with its finding ID and a one-line
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
   boundary (`/test-all`, `@test-runner`), and never silently self-approves an outward-facing action.
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
- **Writes to** `TEST-FINDINGS.md` (the ledger) and the `COMPLETION-PLAN` (the living plan).
- **Complements** `/error-recovery` (stuck-loop breaker — retries), `/fault-diagnosis` (root-cause-before-fix),
  and the `/implement-all` remediation loop. Auto-heal is about *breadth* (don't lose discoveries); those are
  about *depth* (don't thrash on one fix).
- **Invoked** automatically by the loop drivers and on demand as `/auto-heal`.
