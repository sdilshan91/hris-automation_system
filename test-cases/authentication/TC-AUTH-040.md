---
id: TC-AUTH-040
user_story: US-AUTH-006
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-040: Edit or delete a built-in role is rejected

## 1. Test Objective
Verify that the system rejects all attempts to modify or delete built-in roles (Tenant Owner, Tenant Admin, HR Officer, Manager, Employee, Recruiter, Auditor). Built-in roles are seeded during tenant provisioning and must remain immutable to preserve the security baseline.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-6
- Functional Requirements: FR-2, FR-6
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state with all 7 built-in roles seeded.
- User `admin@acme.com` is authenticated with `Tenant Admin` role (has `Role.Manage` permission).
- The `role_id` for built-in roles "Employee" and "Manager" are known.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Built-in role 1 | Employee | is_built_in = true |
| Built-in role 2 | Manager | is_built_in = true |
| Built-in role 3 | Tenant Owner | is_built_in = true |
| Update payload | { "name": "Super Employee", "permissions": ["Payroll.View"] } | Attempt to rename and change permissions |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT. | JWT issued with `Tenant Admin` role. |
| 2 | Send `PUT /api/v1/tenant/roles/{employee_role_id}` with body `{ "name": "Super Employee", "description": "Modified", "permissions": ["Payroll.View"] }`. | HTTP 400 Bad Request. Response body contains error message: "Built-in roles cannot be modified." |
| 3 | Send `DELETE /api/v1/tenant/roles/{employee_role_id}`. | HTTP 400 Bad Request. Response body contains error message: "Built-in roles cannot be deleted." |
| 4 | Send `PUT /api/v1/tenant/roles/{manager_role_id}` with body `{ "name": "Team Lead", "permissions": [] }`. | HTTP 400 Bad Request. Response body contains error message: "Built-in roles cannot be modified." |
| 5 | Send `DELETE /api/v1/tenant/roles/{manager_role_id}`. | HTTP 400 Bad Request. Response body contains error message: "Built-in roles cannot be deleted." |
| 6 | Send `DELETE /api/v1/tenant/roles/{tenant_owner_role_id}`. | HTTP 400 Bad Request. Response body contains error message: "Built-in roles cannot be deleted." |
| 7 | Send `GET /api/v1/tenant/roles` to verify no built-in roles were modified. | HTTP 200 OK. All built-in roles retain their original names, descriptions, and permissions. The `is_built_in` flag is `true` for each. |
| 8 | Verify the UI displays a lock icon and "Built-in" badge on built-in roles. | Built-in role cards show lock icon. Clicking a built-in role opens a read-only permissions view with no edit controls. |
| 9 | Verify that the rejected modification attempts are recorded in the audit log. | Audit log entries exist for each rejected operation with the reason "Built-in roles cannot be modified/deleted." |

## 6. Postconditions
- All built-in roles remain unchanged (names, descriptions, permissions, is_built_in flag).
- No data corruption has occurred in the role or role_permission tables.
- Audit log contains entries for the rejected attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
