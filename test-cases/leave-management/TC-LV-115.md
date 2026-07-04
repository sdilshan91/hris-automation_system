---
id: TC-LV-115
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: automated
created: 2026-06-14
---

# TC-LV-115: Balance correctness across carry-forward, expiry, and adjustments (BR-1 formula)

## 1. Test Objective
Verify that the displayed balance always reconciles to the leave ledger's running `balance_after` across non-trivial combinations, including opening accrual, negative adjustments, and expired carry-forward (BR-1, FR-2, FR-5). BUG-030 regression: the pre-fix dashboard computed `balance` from the entitlement engine's pro-rated value and EXCLUDED `Accrual` ledger entries, so it diverged from the authoritative `leave_ledger.balance_after` (wrong magnitude and sign). The card `balance` must equal the last ledger entry's `balance_after`.

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2, FR-5
- Business Rules: BR-1
- Regression for: BUG-030 (HIGH) — my-balance `balance` diverged from `leave_ledger.balance_after` by excluding `Accrual`.

## Automated Test Binding
- `@TC-LV-115` → `src/backend/HRM.Tests/Integration/LeaveBalanceLedgerReconciliationTests.cs`
  - `MyBalance_IncludesOpeningAccrual_EqualsFinalLedgerEntryBalanceAfter` — reads the final `balance_after` (latest entry by `OccurredAt`) straight from the seeded ledger and asserts the my-balance card `balance` equals it (general reconciliation invariant, not a hard-coded number). Pre-fix the card excludes the opening `Accrual +20` and diverges (fails); post-fix it reconciles (passes).
- Runner: xUnit (real MediatR pipeline + entitlement engine, InMemory provider). Status `automated` until `/verify-fix BUG-030` re-runs it against merged code and flips to `pass`.

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Annual Leave 2026 ledger: entitlement 14, carryForward 5, used 7, expired 1 (unused carry-forward expired), adjustments +2 and -1 (net +1).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Entitlement | 14 | -- |
| Carry Forward | 5 | -- |
| Used | 7 | -- |
| Expired | 1 | -- |
| Adjustments (net) | +1 | +2 and -1 |
| Expected balance | 12 | 14 + 5 - 7 - 1 + 1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard and read the Annual card | entitlement=14, carryForward=5, used=7, expired=1 displayed. |
| 2 | Compute the expected balance per BR-1 | 14 + 5 - 7 - 1 + 1 = 12. |
| 3 | Compare to the card balance and the `my-balance` API `balance` field | Both equal 12; expired and adjustments are reflected (not ignored). |
| 4 | Open the ledger and re-derive balance-after from the entries | The running balance-after of the final entry equals 12, reconciling card and ledger. |
| 5 | Assert reconciliation generally (BUG-030) | The card `balance` equals the latest `leave_ledger` entry's `balance_after` for the leave type/year — the opening `Accrual` is included, never dropped in favour of the engine value. |

## 6. Postconditions
- Balance reconciles to the BR-1 formula including expiry and signed adjustments.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
