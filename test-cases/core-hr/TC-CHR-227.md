---
id: TC-CHR-227
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-227: Manager role is blocked from changing employee status (BR-2, security)

## 1. Test Objective
Verify that a user with the Manager role cannot change an employee's status. Only HR Officers and Tenant Admins are authorized per BR-2. The API must return 403 Forbidden and the UI must not display the "Change Status" button for Managers.

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A Manager user (`manager-uuid`) is authenticated in the "acme" tenant context.
- Employee "John Smith" (`emp-001-uuid`) exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User Role | Manager | Not authorized for status change |
| Employee | John Smith (emp-001-uuid) | Status: active |
| New Status | suspended | Would be valid if authorized |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the Manager user, navigate to the employee profile `/employees/emp-001-uuid`. | Profile loads. The "Change Status" button is NOT visible. |
| 2 | Send `POST /api/v1/tenant/employees/emp-001-uuid/status` with body `{ "newStatus": "suspended", "reason": "Test", "effectiveDate": "2026-06-12" }` using Manager credentials. | Response status is 403 Forbidden. |
| 3 | Verify employee status has not changed. | Employee status remains `active`. No employment history entry was created. |

## 6. Postconditions
- Employee status remains `active`.
- No employment history entries or audit log entries for status change were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
