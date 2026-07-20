---
id: TC-LV-267
user_story: US-LV-006
module: Leave Management
priority: medium
type: performance
status: automated
created: 2026-07-20
automated: 2026-07-20
defect:
  - DF-21
---

# TC-LV-267: My-balance resolves entitlement for all leave types in ONE batched call, not a per-type N+1 (DF-21 — US-LV-006)

## 1. Test Objective
Verify the DF-21 fix on `LeaveDashboardService.GetMyBalancesAsync`: the informational per-type
entitlement is resolved for **every** leave type in a single batched call
(`ILeaveEntitlementService.ComputeProratedEntitlementsBatchAsync`) instead of one
`ComputeEffectiveEntitlementAsync` engine round-trip per leave type (the old N+1). The batched value
must flow through unchanged (same resolver the leave reports use, BUG-124), and a type the batch can't
resolve (probation / ineligible / missing reference data) must still read `0` via the missing-pair
fallback — behaviour-identical to the old per-type failure fallback.

## 2. Related Requirements
- User Story: US-LV-006 (my leave balance dashboard)
- Business Rule: BR-1 (headline balance is the authoritative ledger running balance; entitlement is informational)
- Finding: DF-21 (my-balance per-type entitlement N+1); reuses the batch resolver from BUG-124

## 3. Preconditions
- A tenant with ≥2 active leave types + 1 archived type (the fixture seeds annual / sick / archived).
- `ILeaveEntitlementService` substituted; the batch method armed with an (employee, type) → prorated-days map.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Annual entitlement (batch stub) | 14.00 | informational field on the card |
| Sick entitlement (batch stub) | 7.00 | |
| Archived-type entitlement | 0.00 | archived, zero balance → dropped (BR-3) |
| Leave types in fixture | 3 | old code = 3 engine calls; fix = 1 batch call, 0 per-type calls |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Call `GetMyBalancesAsync(year)` with 3 seeded leave types. | Exactly **1** `ComputeProratedEntitlementsBatchAsync` call and **0** `ComputeEffectiveEntitlementAsync` calls (N+1 removed). | `LeaveDashboardServiceTests.GetMyBalances_ResolvesEntitlementInOneBatchedCall_NoPerTypeN1` |
| 2 | Read the annual card's `Entitlement`. | `14.00` — the batched value flows through unchanged (informational only; not added into Balance). | `LeaveDashboardServiceTests.GetMyBalances_NoLedgerOrRequests_ReturnsActiveTypesWithZeroAndEntitlement` |
| 3 | Null-year (default) path resolves the current leave year. | Cards read the same batched entitlement; `LeaveYear == current`. | `LeaveDashboardServiceTests.GetMyBalances_DefaultsToCurrentYear_WhenYearNull` |
| 4 | Fiscal (month-4) tenant, default leave year off the fixture stub. | Missing (employee, type) pair → entitlement `0`; balance stays ledger-driven (unchanged). | `LeaveDashboardServiceTests.GetMyBalances_FiscalTenant_DefaultLeaveYearComesFromTheClock_NotRawUtcNowYear` |

## 6. Postconditions
- The my-balance card issues a fixed number of entitlement round-trips (2 queries in the batch method)
  regardless of how many leave types the tenant has; no functional change to the surfaced values.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test (missing-pair → 0 fallback, null-year default)
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test (N+1 removal)
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `LeaveDashboardServiceTests.GetMyBalances_ResolvesEntitlementInOneBatchedCall_NoPerTypeN1` (the no-N+1 guard)
  - `LeaveDashboardServiceTests.GetMyBalances_NoLedgerOrRequests_ReturnsActiveTypesWithZeroAndEntitlement` (value flows through)
  - `LeaveDashboardServiceTests.GetMyBalances_DefaultsToCurrentYear_WhenYearNull`
  - `LeaveDashboardServiceTests.GetMyBalances_FiscalTenant_DefaultLeaveYearComesFromTheClock_NotRawUtcNowYear`
- Backing suite trait: `[Trait("DF", "DF-21")]` on the guard test.
