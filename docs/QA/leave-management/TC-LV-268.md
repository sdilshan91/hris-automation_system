---
id: TC-LV-268
user_story: US-LV-002
module: Leave Management
priority: medium
type: functional
status: automated
created: 2026-07-21
automated: 2026-07-21
defect:
  - DF-4
  - BUG-118
---

# TC-LV-268: Leave-entitlement recalc durability — a daily reconciliation sweep self-heals a lost post-commit recalc enqueue, idempotently and tenant-scoped (DF-4 / BUG-118)

## 1. Test Objective
Verify the DF-4 / BUG-118 durability backstop: `LeaveEntitlementService.UpdateRuleAsync` commits a rule
edit then does a best-effort post-commit `Enqueue` of the recalc; if Hangfire storage is unavailable in
that window the enqueue is lost and the leave type's already-accrued employees never converge. The new
recurring `LeaveEntitlementReconcileJob` (daily, `"30 2 * * *"`) re-runs
`RecalculateEntitlementsAsync(currentLeaveYear, leaveTypeId: null)` per **active/trial** tenant via
`ITenantJobRunner`, so a dropped enqueue self-heals within one cadence. Because the recalc is idempotent
(writes an `Adjusted` delta only when the rule-derived target differs from what was granted, and nothing
when the delta is zero), the sweep is a no-op over already-converged tenants and never double-counts. The
existing fast-path enqueue in `UpdateRuleAsync` is unchanged.

## 2. Related Requirements
- User Story: US-LV-002 (leave entitlement rules)
- Acceptance Criteria: AC-5 (a rule change propagates to affected employees)
- Business Rule: recalc is idempotent (delta vs rule-derived target; sentinel-tagged `Adjusted` rows)
- Finding: DF-4 / BUG-118 (best-effort post-commit enqueue can silently drop the recalc)
- Decision (user, 2026-07-21): reconciliation SWEEP backstop (not a transactional outbox — the consumer is idempotent, so exactly-once is unneeded; enqueue-in-transaction is infeasible under the split-connection RLS design)

## 3. Preconditions
- A tenant with an active leave-entitlement rule whose target differs from the current ledger (accrual present, no recalc adjustment yet — simulating a lost enqueue).
- The reconcile job runnable over a real DI graph (real `TenantJobRunner` RLS-off, real `TenantLeaveYearResolver`, real `LeaveEntitlementService`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Accrual present, rule target higher | +5 expected `Adjusted` delta | the convergence the lost enqueue would have produced |
| Second sweep run | 0 new rows | idempotent no-op |
| Tenant A / Tenant B deltas | +5 / +3 | each converges under its own scope |
| Suspended tenant C | not swept | predicate = Active/Trial only |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Seed a tenant whose ledger is stale vs its rule (accrual, no recalc — as if the enqueue was lost); run ONLY the reconcile sweep (no `UpdateRuleAsync`). | The sweep alone writes the `+5` `Adjusted` delta — employees converge. | `LeaveEntitlementReconcileJobTests.ReconcileSweep_converges_a_tenant_whose_recalc_enqueue_was_lost` |
| 2 | Run the sweep a SECOND time over the now-converged tenant. | Exactly ONE `Adjusted` row total — the re-run writes nothing (idempotent, no double-count). | `ReconcileSweep_second_run_writes_nothing_new` |
| 3 | Two active tenants (A→+5, B→+3) + a Suspended tenant C; run the sweep. | A and B each converge under their OWN tenant scope (no cross-tenant leak); C is not swept. | `ReconcileSweep_isolates_tenants_and_skips_inactive` |
| 4 | Run the sweep with a substitute `ITenantJobRunner`. | `RunForTenantAsync` is invoked once per Active/Trial tenant and never for a Suspended tenant. | `ReconcileSweep_invokes_the_runner_once_per_active_tenant` |

## 6. Postconditions
- A rule change converges even if its post-commit recalc enqueue was lost — within one sweep cadence,
  tenant-scoped, without disturbing manual adjustments / overrides / not-yet-accrued employees. Normal-case
  latency is unchanged (the fast-path enqueue still fires immediately).

## 7. Test Category Tags
- [x] Happy path (sweep converges)
- [ ] Negative test
- [x] Boundary test (idempotent second run; inactive tenant skipped)
- [ ] Security test
- [x] Multi-tenant isolation (per-tenant scope, no leak)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), all carrying `[Trait("TC", "TC-LV-268")]`:**
  - `HRM.Tests/Unit/LeaveEntitlementReconcileJobTests.ReconcileSweep_converges_a_tenant_whose_recalc_enqueue_was_lost`
  - `…ReconcileSweep_second_run_writes_nothing_new`
  - `…ReconcileSweep_isolates_tenants_and_skips_inactive`
  - `…ReconcileSweep_invokes_the_runner_once_per_active_tenant`
- The fast-path enqueue remains covered by `LeaveEntitlementServiceTests.UpdateRule_enqueues_entitlement_recalc_bug118` (unchanged).
- Recurring registration: job id `leave-entitlement-reconcile`, cron `"30 2 * * *"` (Program.cs); DI `AddScoped<LeaveEntitlementReconcileJob>`.
