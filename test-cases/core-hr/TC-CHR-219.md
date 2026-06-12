---
id: TC-CHR-219
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-219: Invalid transition terminated to probation via API returns 400 with exact error message (negative)

## 1. Test Objective
Verify that attempting an invalid status transition (terminated -> probation) via the API is rejected with HTTP 400 and the exact error message: "Invalid status transition. Terminated employees cannot be moved to probation." This validates AC-5 and the server-side state machine enforcement (FR-2, BR-1, BR-3).

## 2. Related Requirements
- User Story: US-CHR-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2
- Business Rules: BR-1, BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Former Employee" (`emp-terminated-uuid`) exists with status `terminated`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Former Employee (emp-terminated-uuid) | Status: terminated |
| New Status | probation | Invalid transition |
| Reason | Rehire attempt | Valid reason text |
| Effective Date | 2026-06-12 | Valid date |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-terminated-uuid/status` with body `{ "newStatus": "probation", "reason": "Rehire attempt", "effectiveDate": "2026-06-12" }` using HR Officer credentials. | Response status is 400 Bad Request. |
| 2 | Inspect the response body. | Body contains error message: "Invalid status transition. Terminated employees cannot be moved to probation." (exact wording per AC-5). |
| 3 | Query the employment history table for emp-terminated-uuid. | No new status_change record was created. The employee's status remains `terminated`. |
| 4 | Repeat for all other invalid transitions from terminated: `POST` with newStatus = "active", "suspended", "inactive". | All return 400 with appropriate error messages. No status changes recorded. |

## 6. Postconditions
- Employee status remains `terminated`.
- No employment history entries were created by this test.
- No audit log entries for status change were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
