---
id: TC-CHR-222
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-222: Terminate employee -- user login disabled, excluded from active headcount, payroll exclusion hook triggered (AC-3)

## 1. Test Objective
Verify that when an employee's status is changed to "terminated", the system: (1) deactivates the employee's linked user login, (2) removes them from active headcount reports, (3) excludes them from future payroll runs (via hook/event), and (4) disables self-service portal access. Data is retained per retention policy. This validates AC-3 and FR-5 side effects for termination.

## 2. Related Requirements
- User Story: US-CHR-009
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5
- Business Rules: BR-3, BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Alice Johnson" (`emp-003-uuid`) exists in tenant "acme" with status `active`.
- The employee has a linked user account (`user-003-uuid`) with portal access enabled and can currently log in.
- Active headcount query currently includes this employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Alice Johnson (emp-003-uuid) | Status: active, linked user_id: user-003-uuid |
| New Status | terminated | Valid transition from active |
| Reason | End of contract | Required |
| Effective Date | 2026-06-12 (today) | Immediate |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm active headcount: query `GET /api/v1/tenant/employees?activeOnly=true` or equivalent headcount report. | Alice Johnson appears in the active employee list / headcount count includes her. |
| 2 | Send `POST /api/v1/tenant/employees/emp-003-uuid/status` with body `{ "newStatus": "terminated", "reason": "End of contract", "effectiveDate": "2026-06-12" }`. | Response status is 200 OK. Employee status updated to "terminated". |
| 3 | Verify the employee's linked user account is deactivated. | Query the `users` table: `user-003-uuid` has `is_active = false` (or equivalent flag). |
| 4 | Attempt to log in as Alice Johnson using her credentials. | Login is rejected with an appropriate error (e.g., "Your account has been deactivated" or "Invalid credentials"). |
| 5 | Query the active headcount again: `GET /api/v1/tenant/employees?activeOnly=true`. | Alice Johnson no longer appears in the active list. The count is decremented by one. |
| 6 | Verify payroll exclusion: check that a payroll exclusion event/hook was dispatched or that the employee is flagged for payroll exclusion. | DEFERRED -- Payroll module not yet built. Verify that either: (a) an event/message `EmployeeTerminated` was published to the event bus, or (b) a `payroll_exclusion` flag/record exists, or (c) the integration hook endpoint was called. If none of these exist yet, verify the employee's status is `terminated` which the payroll module will use to exclude them when built. |
| 7 | Verify offboarding workflow trigger (if configured). | DEFERRED -- Offboarding module not yet built. Check logs for offboarding workflow trigger event if implemented. |
| 8 | Verify employee data is retained (not hard-deleted). | Query `employees` table directly: record exists with `is_deleted = false`, `status = terminated`. All personal data fields are intact per retention policy. |
| 9 | Verify the employee's profile is still viewable by HR Officer. | Navigate to `/employees/emp-003-uuid`. Profile loads with "Terminated" badge in red (`bg-red-100 text-red-800`). All data sections are visible (read-only). |

## 6. Postconditions
- Employee status is "terminated" in the database.
- Linked user account is deactivated.
- Employee excluded from active headcount queries.
- Employment history contains the termination status_change entry.
- Employee data is retained (not deleted).
- Payroll exclusion hook dispatched (or deferred for payroll module).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
