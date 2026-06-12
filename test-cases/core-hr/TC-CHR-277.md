---
id: TC-CHR-277
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-277: Manager termination triggers HR reassignment reminder notification

## 1. Test Objective
Verify that when a manager with direct reports is terminated or suspended, the system sends a notification to HR prompting them to reassign the manager's direct reports. This validates BR-4. Note: actual notification dispatch is DEFERRED pending the Notification module; this test verifies the trigger mechanism and logging.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-4
- Dependencies: US-CHR-009 (Employee Status Management)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Manager M exists with status `active`.
- Employees E1, E2, E3 have `reports_to_employee_id` = M.id (3 direct reports).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager M | mgr@acme.test | Active, has 3 direct reports |
| Employee E1 | e1@acme.test | Reports to M |
| Employee E2 | e2@acme.test | Reports to M |
| Employee E3 | e3@acme.test | Reports to M |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Manager M has 3 direct reports via `GET /api/v1/tenant/employees/{M.id}/direct-reports`. | Returns E1, E2, E3. |
| 2 | Change Manager M's status to "terminated" via the status change flow (US-CHR-009). | Status change succeeds. Manager M is now terminated. |
| 3 | Verify the system generated a notification/reminder for HR to reassign M's direct reports. | A log entry or notification record exists indicating "Manager [M name] terminated with 3 direct reports. Reassignment required." (Notification dispatch DEFERRED pending Notification module.) |
| 4 | Verify E1, E2, E3 still have `reports_to_employee_id` = M.id. | The direct reports are NOT automatically reassigned; they retain the terminated manager until HR manually reassigns. |

## 6. Postconditions
- Manager M is terminated.
- E1, E2, E3 still reference M as their manager (stale reference until HR acts).
- An HR reassignment reminder was triggered (logged, notification dispatch DEFERRED).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
