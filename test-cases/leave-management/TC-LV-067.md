---
id: TC-LV-067
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-067: Manager with no direct reports (or no pending requests) sees an empty queue, not an error

## 1. Test Objective
Verify that a Manager who has the `Leave.Approve.Team` permission but no direct reports — or whose direct reports have no pending requests — sees a friendly empty state in the pending queue rather than an error, and that the API returns an empty result set with a zero total count.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-4
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Dana Cruz" is authenticated and has `Leave.Approve.Team`.
- Scenario A: Dana has zero direct reports (`manager_employee_id` points to nobody).
- Scenario B: Dana has direct reports, but none of them has a pending leave request (all are approved/rejected/none).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | Dana Cruz | Leave.Approve.Team granted |
| Scenario A | 0 direct reports | Empty team |
| Scenario B | 2 direct reports, 0 pending requests | No pending |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (Scenario A), navigate to the Leave Approvals page | `GET /api/v1/leaves/pending` returns 200 with an empty `items` array and `totalCount: 0`. |
| 2 | Observe the UI | A clear empty-state message is shown (e.g. "No pending leave requests"); no spinner stuck, no error toast, no stack trace. |
| 3 | As Dana (Scenario B), navigate to the page | `GET /api/v1/leaves/pending` returns 200 with `items: []` and `totalCount: 0`. |
| 4 | Observe the UI for Scenario B | Same empty-state message; pagination controls are hidden or disabled. |
| 5 | Verify no cross-team leakage in either scenario | Zero requests from employees who do not report to Dana appear (BR-1). |

## 6. Postconditions
- No data mutated.
- Empty state is handled gracefully in both no-reports and no-pending scenarios.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
