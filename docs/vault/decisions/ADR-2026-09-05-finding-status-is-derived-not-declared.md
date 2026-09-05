---
type: decision
status: accepted
created: 2026-09-05
deciders: product owner + Claude (auto-heal tracking review)
tags: [qa, ledger, auto-heal, tooling, adr-lite]
---

# Out-of-lane finding status is DERIVED from the repo, never declared on the finding

## Context

The request was for lifecycle labels on out-of-lane findings — `auto-healed-pending`,
`documented-completed` and so on — so their status could be tracked easily.

Two problems with taking that literally:

1. **The examples span two independent axes.** Whether the auto-heal protocol ran
   (documented → scheduled → deferred/closed) is orthogonal to whether the defect is fixed
   (OPEN → FIXED → VERIFIED). Collapsing them into one label makes both unanswerable.
2. **A hand-maintained field rots, and this repo had two live proofs the same day.** The ledger's own
   Summary table read **169 total while 218 findings existed** — stale by 49, in the index a reader
   sees *first*, with nothing checking it. And **14 findings sat documented-but-unscheduled** with
   nothing noticing. Both are exactly the failure the label was meant to prevent.

## Decision

Model **two axes**, and **derive both** from the repository rather than declaring them:

```
HEAL — did the auto-heal protocol run?   documented -> scheduled -> deferred/closed
FIX  — is the defect fixed?              OPEN -> FIXED -> VERIFIED
```

- *documented* = the finding has a `### <ID>` entry in a ledger
- *scheduled* = its id is referenced in `GAP-CLOSURE-QUEUE.md`
- *deferred* = its own status line says PARKED / needs-decision / WONTFIX
- *archived* = it lives in `TEST-FINDINGS-RESOLVED.md`

Nothing is hand-maintained, so nothing can go stale, and **218 existing findings needed no backfill**.
`scripts/findings-status.sh` reports both axes; `TheSummaryTable_MatchesTheActualCounts` asserts the
ledger's front page against reality.

## Alternatives considered

- **An explicit `Heal:` field on every finding** — most readable at a glance, and rejected: it needs a
  218-entry backfill, and every future finding must remember to set *and update* it. That is precisely
  the discipline that had already failed twice that day.
- **Explicit on new findings, derived for the backlog** — avoids the backfill but leaves two mechanisms
  describing one thing, and readers must know which applies. That ambiguity is what `ISSUE-465` and the
  duplicate queue rows were both about.

## Consequences

- Status is always current by construction; there is no "someone forgot to update it" state.
- The cost moves into the *derivation rules*: if the queue file is renamed, or a finding is scheduled
  somewhere else, the report silently under-counts. The rules are therefore kept in one script with
  their reasoning in the header, not spread across callers.
- **It immediately surfaced something larger than the question asked**: across the whole live ledger,
  **100 of 218 findings were documented but never scheduled** — nearly half the backlog stored rather
  than tracked. That number was invisible before and is now one command away.
- A 20-finding sample of those 100 then measured **15% wholly wasted if scheduled** (already fixed or
  obsolete) and **30%** counting rows that merely overstate remaining work — and showed the prior
  "29% stale" headline is ambiguous between two figures differing by 2×.

## Links
- Related code: `scripts/findings-status.sh`, `LedgerTraceabilityTests.TheSummaryTable_MatchesTheActualCounts`
- Related findings: `ISSUE-463`, `ISSUE-465`, `ISSUE-498`
- PR: #623
