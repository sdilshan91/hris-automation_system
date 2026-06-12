---
id: TC-CHR-228
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-228: Employee role is blocked from changing their own or any employee's status (BR-2, security)

## 1. Test Objective
Verify that a user with the Employee role cannot change any employee's status (including their own). The API must return 403 Forbidden and the UI must not display the "Change Status" button. This validates BR-2.

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An Employee user (`employee-user-uuid`) is authenticated in the "acme" tenant context, linked to employee record `emp-self-uuid`.
- Another employee "John Smith" (`emp-001-uuid`) exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User Role | Employee | Not authorized for status change |
| Self Employee | emp-self-uuid | The logged-in employee's own record |
| Other Employee | John Smith (emp-001-uuid) | Another employee in same tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the Employee user, navigate to their own profile. | Profile loads. The "Change Status" button is NOT visible. |
| 2 | Send `POST /api/v1/tenant/employees/emp-self-uuid/status` with body `{ "newStatus": "inactive", "reason": "Self request", "effectiveDate": "2026-06-12" }` using Employee credentials. | Response status is 403 Forbidden. |
| 3 | Send `POST /api/v1/tenant/employees/emp-001-uuid/status` with the same body using Employee credentials. | Response status is 403 Forbidden. |
| 4 | Verify no status changes occurred. | Both employees retain their original statuses. No employment history entries created. |

## 6. Postconditions
- No status changes occurred.
- No employment history or audit entries were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
