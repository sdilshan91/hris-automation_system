---
name: reference-gap-tracking-sources
description: Where HRM gap-analysis state lives — three competing documents with conflicting GAP-ID numbering; which to trust as a claim source
metadata:
  type: reference
---

Gap tracking for this repo is spread across **three** documents, and they do not agree:

- `docs/Architecture/gap-analysis/GAP-REGISTER.md` — the GAP-001..040 register. Snapshot dated
  **2026-08-08**; its own banner concedes the headline counts do not survive measurement.
- `docs/Architecture/gap-analysis/REFRESH-2026-08-17.md` — a later re-verification that **reclassifies**
  several rows (e.g. GAP-019b is Google/Apple sign-in, not billing; GAP-020's headline is wrong;
  GAP-033b is out-of-repo by design).
- `docs/QA/plans/GAP-CLOSURE-QUEUE.md` — the work queue with `[x]`/`[ ]` per gap and PR numbers.

**Trap:** commit messages reference GAP IDs from a *different* numbering than the register. Example:
`dc9a9965 ... (GAP-035 §6.11-a)` is about user_id/trace_id log stamping, while register GAP-035 is
"per-tenant sender identity"; `293af88a ... (GAP-036)` is a dev field-encryption key, while register
GAP-036 is per-user GDPR erasure. Never map a commit to a register row by ID alone — read the diff.

All three are **claims**, never evidence. See [[feedback-verdicts-come-from-src]].
