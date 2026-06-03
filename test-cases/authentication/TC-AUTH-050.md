---
id: TC-AUTH-050
user_story: US-AUTH-006
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-050: Role and permission changes are audited in tenant audit log

## 1. Test Objective
Verify that all RBAC-related operations (role creation, role update, role deletion, role assignment/removal, and authorization failures) are recorded in the tenant audit log with sufficient detail for security monitoring and compliance. This covers both FR-7 (audit logging of role/permission changes) and NFR-4 (authorization failure logging).

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-2, AC-3, AC-4, AC-6
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- User `employee@acme.com` has the `Employee` role.
- The tenant audit log is accessible (via API or admin UI).
- Audit log table/service is operational and capturing events.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Performs RBAC operations |
| Employee user | employee@acme.com | Subject of role changes; also triggers 403 |
| Custom role name | Audit Test Role | Created for this test |
| Permissions | Report.View, Report.Export | Assigned to the test role |
| Audit log endpoint | GET /api/v1/tenant/audit-log | Or equivalent admin endpoint |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and note the current timestamp. | JWT issued. Timestamp recorded as baseline for log queries. |
| 2 | Create a custom role: `POST /api/v1/tenant/roles` with `{ "name": "Audit Test Role", "permissions": ["Report.View", "Report.Export"] }`. | HTTP 201 Created. |
| 3 | Query the audit log for events after the baseline timestamp. | Audit log contains an entry: action = "role.created", actor = admin@acme.com, target = "Audit Test Role", details include permission list, tenant_id = acme. |
| 4 | Update the role: `PUT /api/v1/tenant/roles/{role_id}` adding permission `Attendance.View`. | HTTP 200 OK. |
| 5 | Query the audit log. | New entry: action = "role.updated", actor = admin@acme.com, target = "Audit Test Role", details include the permission change (added: Attendance.View). |
| 6 | Assign the role to `employee@acme.com`: `PATCH /api/v1/tenant/users/{employee_id}` with `{ "roleIds": ["{employee_role_id}", "{audit_test_role_id}"] }`. | HTTP 200 OK. |
| 7 | Query the audit log. | New entry: action = "role.assigned", actor = admin@acme.com, target_user = employee@acme.com, role = "Audit Test Role". |
| 8 | Authenticate as `employee@acme.com` and attempt to access `GET /api/v1/tenant/payroll/runs` (lacks Payroll.View). | HTTP 403 Forbidden. |
| 9 | Query the audit log (or security log). | Entry for authorization failure: action = "authorization.denied", user = employee@acme.com, endpoint = /api/v1/tenant/payroll/runs, missing_permission = "Payroll.View", tenant_id = acme. |
| 10 | Remove the role from `employee@acme.com`: `PATCH /api/v1/tenant/users/{employee_id}` with `{ "roleIds": ["{employee_role_id}"] }`. | HTTP 200 OK. |
| 11 | Query the audit log. | Entry: action = "role.unassigned", actor = admin@acme.com, target_user = employee@acme.com, role = "Audit Test Role". |
| 12 | Delete the custom role: `DELETE /api/v1/tenant/roles/{role_id}`. | HTTP 200 OK (or 204 No Content). |
| 13 | Query the audit log. | Entry: action = "role.deleted", actor = admin@acme.com, target = "Audit Test Role", tenant_id = acme. |
| 14 | Verify all audit entries include: timestamp, actor ID, action type, target details, tenant ID, and IP address. | All required fields are present in every audit log entry. No PII leakage beyond what is necessary for auditing. |

## 6. Postconditions
- Audit log contains a complete chronological record of all RBAC operations performed during this test.
- Each entry has sufficient detail for security forensics and compliance review.
- Authorization denial events are logged separately or tagged for security monitoring dashboards.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
