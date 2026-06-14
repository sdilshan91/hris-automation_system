---
id: TC-LV-195
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-195: A REJECTED leave cannot be cancelled (negative; BR-2)

## 1. Test Objective
Verify that attempting to cancel a leave request already in the `Rejected` terminal state is rejected (it is not an eligible state for cancellation), with no status change and no ledger entry (BR-2).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-2
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has a leave request R that was previously `Rejected` by her manager (no `used` ledger entry was ever created for a rejected request).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Annual Leave, status Rejected | terminal, ineligible |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane, attempt `POST /api/v1/leaves/{R}/cancel` with a reason | Rejected with a clear "only pending or approved requests can be cancelled" / invalid-state error (HTTP 400/409). |
| 2 | Inspect R's status | Unchanged -- still `Rejected`; no `cancelled_at`. |
| 3 | Query `leave_ledger` | No new row written. |
| 4 | (UI) Verify the cancel affordance | Cancel button is hidden/disabled for a Rejected request with an explanatory tooltip (Section 8). |

## 6. Postconditions
- The rejected request stays Rejected; cancellation is refused; no ledger impact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
