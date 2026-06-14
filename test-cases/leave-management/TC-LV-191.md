---
id: TC-LV-191
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-191: Reversal ledger entry restores the exact deducted amount; running balance and dashboard reflect the restored total (boundary -- balance arithmetic)

## 1. Test Objective
Verify the reversal `adjusted` entry created on an approved-leave cancellation restores exactly the days previously deducted (including half-day fractions), the `balance_after` running total is arithmetically correct, and the US-LV-006 balance dashboard reflects the restored balance after cancellation (AC-2, FR-3, dependency on US-LV-006).

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3
- Dependencies: US-LV-006 (dashboard must reflect updated balance)

## 3. Preconditions
- Employee "Jane Smith" (tenant "acme") has two approved future requests on the same leave type (Annual Leave):
  - R1: 3.0 days (`used -3.00`)
  - R2: 0.5 day half-day (`used -0.50`)
- Entitlement 14; after both deductions the balance is 10.50.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R1 days | 3.0 | full days |
| R2 days | 0.5 | half-day fraction |
| Balance before any cancel | 10.50 | 14 - 3.0 - 0.5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cancel R1 with a reason | `adjusted +3.00` row written; `balance_after = 13.50`; running balance = 13.50. |
| 2 | Cancel R2 (half-day) with a reason | `adjusted +0.50` row written (exact half-day restored, not rounded to whole); `balance_after = 14.00`. |
| 3 | Re-read the LeaveLedger running balance | Balance = 14.00 (fully restored to entitlement; reversal magnitudes exactly equal the original deductions). |
| 4 | Open the US-LV-006 balance dashboard / `GET /api/v1/leaves/my-balance` | The Annual Leave card shows the restored balance (14.00); the `used` total reflects only still-active leave (now 0 for this type). |
| 5 | Verify no double-restoration | Re-reading the ledger shows exactly one `adjusted` reversal per cancelled request (not duplicated). |

## 6. Postconditions
- Each cancellation restores exactly its deducted amount; running balance and dashboard agree at 14.00; reversals are 1:1 with cancellations.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
