---
id: TC-CHR-092
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-092: Unauthorized role cannot create employees -- 403

## 1. Test Objective
Verify that a user with an insufficient role (e.g., Employee role, no HR permissions) cannot create employee records. The API should return 403 Forbidden.

## 2. Related Requirements
- User Story: US-CHR-001
- Preconditions: "HR Officer is authenticated"
- Authentication & Authorization module (RBAC)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with "Employee" role only (no HR Officer or Tenant Admin permissions) is authenticated in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Insufficient permissions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as a user with "Employee" role in "acme" | Session established. |
| 2 | Send `POST /api/v1/tenant/employees` with valid employee data | 403 Forbidden. |
| 3 | Verify the error response indicates insufficient permissions | Error message: "You do not have permission to perform this action." or similar. |
| 4 | Verify no employee record was created in the database | No new records. |
| 5 | Verify the "Add Employee" button is hidden in the UI for this role | Role-based UI rendering hides the action. |

## 6. Postconditions
- No employee record is created.
- The Employee-role user cannot access the creation endpoint.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
