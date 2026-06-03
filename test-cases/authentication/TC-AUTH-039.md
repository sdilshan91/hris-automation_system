---
id: TC-AUTH-039
user_story: US-AUTH-006
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-039: Create custom role, assign to user, verify permitted and blocked access

## 1. Test Objective
Verify the end-to-end happy path: a tenant admin creates a custom role with specific permissions, assigns it to a user, and the user can access endpoints covered by those permissions while being blocked from endpoints outside the granted set. This validates the full RBAC lifecycle from role creation through JWT issuance to authorization enforcement.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-2, AC-3, AC-4
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-5, FR-6
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role (has `Role.Manage` and `User.Manage` permissions).
- User `newuser@acme.com` exists in tenant "acme" with the `Employee` role only (no leave management permissions).
- Built-in roles are seeded. No custom role named "Leave Coordinator" exists yet.
- The permission catalog includes `Leave.View`, `Leave.Approve.Team`, and `Payroll.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Target user | newuser@acme.com | Initially Employee role only |
| Custom role name | Leave Coordinator | New custom role to create |
| Custom role description | Can view and approve team leave requests | Descriptive text |
| Permissions to assign | Leave.View, Leave.Approve.Team | Subset of leave permissions |
| Permitted endpoint | GET /api/v1/tenant/leave/requests | Requires Leave.View |
| Blocked endpoint | GET /api/v1/tenant/payroll/runs | Requires Payroll.View |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` at `acme.yourhrm.com` and obtain JWT. | JWT issued with `Tenant Admin` role and admin permissions. |
| 2 | Send `POST /api/v1/tenant/roles` with body `{ "name": "Leave Coordinator", "description": "Can view and approve team leave requests", "permissions": ["Leave.View", "Leave.Approve.Team"] }`. | HTTP 201 Created. Response contains the new role with `role_id`, `tenant_id` matching acme, `is_built_in: false`, and the two permissions listed. |
| 3 | Send `GET /api/v1/tenant/roles` and verify the new role appears. | HTTP 200 OK. Role list includes "Leave Coordinator" alongside built-in roles. Permission count shows 2 and user count shows 0. |
| 4 | Send `PATCH /api/v1/tenant/users/{newuser_id}` with body `{ "roleIds": ["{employee_role_id}", "{leave_coordinator_role_id}"] }`. | HTTP 200 OK. The user's `user_tenant_role` records are updated to include both Employee and Leave Coordinator roles. |
| 5 | Authenticate as `newuser@acme.com` (or trigger a token refresh) to obtain a fresh JWT. | New JWT issued. |
| 6 | Decode the JWT and inspect claims. | `roles` claim contains `["Employee", "Leave Coordinator"]`. `permissions` claim includes `Leave.View` and `Leave.Approve.Team` in addition to Employee-level permissions. |
| 7 | Send `GET /api/v1/tenant/leave/requests` using the new JWT. | HTTP 200 OK. User can access the leave requests endpoint. |
| 8 | Send `GET /api/v1/tenant/payroll/runs` using the same JWT. | HTTP 403 Forbidden. User lacks `Payroll.View` permission. Response body indicates insufficient permissions. |
| 9 | Verify the authorization failure from step 8 is logged. | Audit/security log entry contains user_id, endpoint `/api/v1/tenant/payroll/runs`, and missing permission `Payroll.View`. |

## 6. Postconditions
- Custom role "Leave Coordinator" exists in tenant "acme" with 2 permissions and 1 assigned user.
- User `newuser@acme.com` has roles Employee and Leave Coordinator.
- User can access leave endpoints but not payroll endpoints.
- Role creation and assignment are recorded in the tenant audit log.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
