---
id: TC-LV-119
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-119: New joiner with no ledger data sees a friendly empty state

## 1. Test Objective
Verify that an employee with no computed leave balance (new joiner, no accrual/ledger entries yet) is shown a friendly empty-state message with an illustration rather than blank cards or an error (AC-5).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" active; employee "Sam Newman" is a newly created active employee with zero `leave_ledger` entries and no entitlement computed yet.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Sam Newman | New joiner, no ledger |
| Ledger entries | 0 | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Sam, navigate to the Leave Balance Dashboard | `GET /api/v1/leaves/my-balance` returns 200 with an empty (or all-zero) result; the request does not error. |
| 2 | Observe the dashboard body | A centered empty-state is shown with the message "Your leave balances are being set up. Please check back soon." and an illustration. |
| 3 | Verify no broken UI | No empty/NaN cards, no progress-bar division-by-zero errors, and no console errors. |
| 4 | Verify Upcoming Leaves with no requests | The Upcoming Leaves section shows its own benign empty state (no error). |

## 6. Postconditions
- Empty state renders gracefully for new joiners.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
