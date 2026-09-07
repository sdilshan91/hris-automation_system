---
name: reference-entitlement-recalc-enqueue
description: LeaveEntitlementService recalc-enqueue seam (ENH-001) — the five mutation sites, the batch-not-per-rule rule, override-own-year scoping, and the non-atomic bulk gap
metadata:
  type: reference
---

`LeaveEntitlementService` enqueues `ILeaveEntitlementRecalcJobScheduler` from **five** mutation sites via one
private `EnqueueRecalcAsync(leaveTypeId, leaveYear, reason, ct)` helper. Before ENH-001 only `UpdateRuleAsync`
did, so the other four leaked stale balances until the 02:30 `LeaveEntitlementReconcileJob` sweep.

Rules the seam encodes — re-check them if you add a sixth site:

- **Enqueue AFTER `SaveChangesAsync`, never before.** The Hangfire worker restores its own tenant scope and
  re-reads from the DB, so a pre-commit enqueue races the job against uncommitted state. None of these methods
  run inside an ambient transaction, so there is no rollback that could strand a scheduled job.
- **Batch mutations get ONE job, not one per row.** The recalc is scoped by `(leaveYear, leaveTypeId)` and is
  idempotent — it is *not* keyed by rule id — so N enqueues are N duplicate sweeps. A 200-row bulk import
  would flood Hangfire for zero extra correctness.
- **Overrides pass their OWN `LeaveYear`; rules pass null** (→ `ITenantLeaveYearResolver.LabelForAsync(today)`).
  Overrides are year-scoped, so back-dating one must recalc *that* year. Copy-pasting the rule path's
  current-year resolution onto an override is a silent wrong-year bug — the tests pin it with `UtcNow.Year + 1`.
- **`CreateRuleAsync` deliberately does NOT enqueue** — `BulkCreateRulesAsync` loops it, so an enqueue there
  would reintroduce the flood. That leaves single-rule create as a real (filed) gap.
- **`BulkCreateRulesAsync` is not atomic**: it calls `CreateRuleAsync`, which `SaveChangesAsync`es per rule, so
  a mid-batch failure leaves earlier rules persisted while returning a failure Result and enqueuing nothing.

The scheduler is a trailing-optional ctor param (null in unit tests / non-Hangfire hosts) and every call site
uses `?.` — a null scheduler is a **silent** no-op with the nightly reconcile job as the only safety net.
See [[feedback-guards-must-be-mutation-proven]] for how these arms were proven.
