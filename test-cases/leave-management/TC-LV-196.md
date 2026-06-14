---
id: TC-LV-196
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-196: An ALREADY-CANCELLED leave cannot be cancelled again -- idempotency / no double reversal (negative; BR-2)

## 1. Test Objective
Verify that a second cancellation attempt on a request already in `Cancelled` state is refused, so the reversal `adjusted` ledger entry is NOT written twice and the balance is not over-restored (BR-2, FR-3).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-2
- Functional Requirements: FR-2, FR-3

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has an approved-then-cancelled request R: cancelled via TC-LV-190, with exactly one `adjusted +3.00` reversal row and balance restored to 11.00.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Annual Leave, status Cancelled | already cancelled |
| Existing reversal | adjusted +3.00 | exactly one |
| Balance | 11.00 | already restored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane, attempt `POST /api/v1/leaves/{R}/cancel` again | Rejected with an invalid-state / already-cancelled error (HTTP 400/409); no second cancellation applied. |
| 2 | Query `leave_ledger` for R | Still exactly ONE `adjusted +3.00` reversal row -- no second reversal (no double-restore). |
| 3 | Re-read Jane's balance | Remains 11.00 (not 14.00 -- no over-restoration). |
| 4 | Inspect approval-history | Still exactly one `action = Cancelled` row (not duplicated). |

## 6. Postconditions
- The double-cancel is refused; the ledger and balance are unchanged by the repeat attempt (idempotent guard).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
