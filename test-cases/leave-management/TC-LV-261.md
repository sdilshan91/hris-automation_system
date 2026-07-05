---
id: TC-LV-261
user_story: US-LV-006
module: Leave Management
priority: high
type: integration
status: automated
created: 2026-07-05
---

# TC-LV-261: Negative-balance FLOOR enforced at approval — approval past `-negative_balance_limit` is rejected (BUG-029 regression)

## 1. Test Objective
Verify that when a leave type permits a negative balance (`negative_balance_allowed = true`) but caps it with a `negative_balance_limit`, `LeaveRequestService.ApproveAsync` rejects any approval whose projected balance would fall **below** the floor `-negative_balance_limit` (400, `negative_balance_limit_exceeded`), while still allowing an approval that lands **exactly on** the floor (`projected == -limit`). Regression guard for BUG-029: before the fix the service only blocked negatives when the type disallowed them entirely, so a negative-allowed type could be driven arbitrarily negative.

## 2. Related Requirements
- User Story: US-LV-006 (per the BUG-029 filing)
- Acceptance Criteria: AC-1 (balance remaining is authoritative)
- Cross-references: US-LV-001 FR-2 / column `negative_balance_limit` (the floor config); US-LV-005 approval flow (where the floor is enforced)
- Defect: BUG-029

## 3. Preconditions
- A leave type with `negative_balance_allowed = true` and `negative_balance_limit = 2` days.
- An employee whose accrued balance is near zero (1 day), reporting to an approving manager.
- Automated: an xUnit integration test drives the real MediatR pipeline → `LeaveRequestService` over a real `AppDbContext` (InMemory) with the tenant global query filter — no service mock.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| negative_balance_allowed | true | type permits going negative |
| negative_balance_limit | 2.00 | floor: balance may reach -2, no further |
| Accrued balance | 1.00 | ledger "Accrual" for the report |
| Reject request | 4 days | projected 1 - 4 = -3 → below the -2 floor |
| Control request | 3 days | projected 1 - 3 = -2 → exactly the floor (inclusive) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Manager approves the 4-day request | Rejected: 400, error contains `negative_balance_limit_exceeded`. |
| 2 | Inspect the request + ledger after the rejected approval | Request stays `Pending`; no `Used` ledger row references it; the ledger balance is unchanged (still 1 day). |
| 3 | Manager approves the 3-day request (projected == -2) | Approved: 200; result `BalanceAfter == -2`. |
| 4 | Inspect the ledger after the successful approval | A `Used` ledger row of `-3` with `balance_after == -2` is persisted; request is `Approved`. |

## 6. Postconditions
- The floor is enforced inclusively: approvals are allowed down to `-negative_balance_limit` and rejected past it. No partial mutation on a rejected approval.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation Binding
- Suite: `src/backend/HRM.Tests/Integration/NegativeBalanceLimitApprovalTests.cs`
- Tests: `Approve_ExceedsNegativeBalanceLimit_IsRejected_BUG029` (fails pre-fix — approval succeeds without the floor check), `Approve_AtNegativeBalanceFloor_Succeeds_BUG029` (control — guards against over-rejection at the floor)
