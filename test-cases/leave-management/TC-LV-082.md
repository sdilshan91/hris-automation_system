---
id: TC-LV-082
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-082: User without Leave.Approve.Team permission is denied access to the pending queue

## 1. Test Objective
Verify that an authenticated user who lacks the `Leave.Approve.Team` permission is denied access to the pending leave queue endpoint and UI, receiving a 403 Forbidden, even if they are an employee within the tenant.

## 2. Related Requirements
- User Story: US-LV-004
- Preconditions: Section 2 (`Leave.Approve.Team` required)
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" is active.
- User "Jane Smith" is an authenticated Employee WITHOUT `Leave.Approve.Team` (she is an individual contributor, not a manager).
- User "Tom Reed" is authenticated with a non-leave role lacking the permission.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Non-approver A | Jane Smith (Employee) | No Leave.Approve.Team |
| Non-approver B | Tom Reed | Role lacks permission |
| Endpoint | GET /api/v1/leaves/pending | Approver-only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane (no `Leave.Approve.Team`), call `GET /api/v1/leaves/pending` | Response 403 Forbidden; no pending-queue data is returned. |
| 2 | As Tom, call the same endpoint | Response 403 Forbidden. |
| 3 | As Jane, attempt to load the Leave Approvals page in the UI | The route is guarded; the page is not rendered (redirect/403/unauthorized view); no API data leaks. |
| 4 | Verify the denial is permission-based, not role-name-based | The check is on the `Leave.Approve.Team` permission so any role granted it passes and any role without it is denied. |
| 5 | Confirm a legitimate approver still succeeds (control) | Manager "Robert Lee" with `Leave.Approve.Team` receives 200 -- the deny is specific to missing permission. |

## 6. Postconditions
- No data mutated.
- Pending-queue access requires `Leave.Approve.Team`; unauthorized users get 403.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
