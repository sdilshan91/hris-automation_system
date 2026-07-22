---
id: TC-LV-269
user_story: US-LV-008
module: Leave Management
priority: medium
type: functional
status: automated
created: 2026-07-22
automated: 2026-07-22
defect:
  - DF-62-parity
---

# TC-LV-269: Encashment-gate vs year-end forfeiture ceiling parity — aligned bases agree, divergence is exactly the accrual-vs-entitlement gap (DF-62-parity)

## 1. Test Objective
Characterize and pin the DF-62-parity relationship: the **leave-encashment gate** caps forfeitable days on the ledger running balance (latest `BalanceAfter`), while the **year-end forfeiture** derives from the engine `ProratedEntitlementDays`; the two agree only when `Σaccruals == ProratedEntitlementDays`. This guard proves (a) under the aligned condition the two forfeitable ceilings are **equal**, and (b) under divergence the difference is **exactly** the accrual-vs-entitlement gap — characterizing the KNOWN, ACCEPTED, user-deferred employee-detriment edge (no double-pay) so a future change that unifies the bases or breaks one derivation is caught, and so nobody mistakes the divergent-arm assertion for a bug to "fix."

## 2. Related Requirements
- User Story: US-LV-008 (Leave Carry-Forward and Expiry Rules)
- Related: US-PAY-010 (leave encashment gate — `LeaveEncashmentService.ProcessAsync`)
- Business Rule: forfeitable = `Compute(balance, CarryForwardLimit).ForfeitedDays`; parity holds iff `Σaccruals == ProratedEntitlementDays`
- Finding: DF-62-parity (accepted encashment/forfeiture divergence — user-deferred the unify, 2026-07-22)

## 3. Preconditions
- InMemory-through-real-EF; the entitlement engine stubbed so `ProratedEntitlementDays` is decoupled from the accrual ledger (that decoupling is the divergence lever). Both ceilings use the REAL derivations (`LeaveCarryForwardService.PreviewYearEndAsync` + `LeaveCarryForwardCalculator.Compute`), anchored to the live `LeaveEncashmentService.ProcessAsync` 422 boundary.

## 4. Test Data
| Scenario | entitlement | Σaccruals | used | limit | encashment ceiling | year-end forfeitable | result |
|----------|-------------|-----------|------|-------|--------------------|----------------------|--------|
| Aligned | 14 | 14 | 2 | 5 | 7 | 7 | equal (parity) |
| Divergent | 14 | 10 | 2 | 5 | 3 | 7 | differ by 4 = 14−10 |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Aligned: seed accrual Σ == engine prorated entitlement; compute both ceilings via the real derivations + the live encashment 422 boundary. | Both forfeitable ceilings == 7 (parity holds); the `encashment_exceeds_balance` gate flips at exactly the ceiling. | `LeaveForfeitureParityTests.Aligned_AccrualsEqualEntitlement_EncashmentAndYearEndForfeitable_Match` |
| 2 | Divergent: introduce under-accrual (Σaccruals 10 < entitlement 14); compute both ceilings. | Encashment 3 vs year-end 7; the difference == 4 == entitlement(14) − Σaccruals(10). Documented as the accepted employee-detriment edge (no double-pay). | `LeaveForfeitureParityTests.Divergent_AccrualsBelowEntitlement_CeilingsDiffer_ByExactlyTheAccrualEntitlementGap` |

## 6. Postconditions
- The two forfeiture ceilings' agreement (aligned) and bounded divergence (accrual-vs-entitlement gap) are pinned; a silent unification or one-sided drift reds the guard. The divergence remains an accepted, documented edge (see `docs/vault/modules/leave-management.md`).

## 7. Test Category Tags
- [x] Happy path (aligned parity)
- [x] Negative test (characterized divergence)
- [x] Boundary test (encashment 422 gate at exactly the ceiling)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by** (`[Trait("TC","TC-LV-269")]`): `HRM.Tests/Integration/LeaveForfeitureParityTests` (2 arms). Auditors: enforcer WIRED (real derivations anchored to the live gate), authenticator AUTHENTIC (both mutation-resistant).
- Decision rationale + the accepted-divergence note: `docs/vault/modules/leave-management.md` (DF-62-parity section).
