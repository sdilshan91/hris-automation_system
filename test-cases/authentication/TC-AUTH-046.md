---
id: TC-AUTH-046
user_story: US-AUTH-006
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-046: Deleting a custom role removes it from assigned users and updates JWT on refresh

## 1. Test Objective
Verify that when a custom role is deleted, it is removed from all user assignments in `user_tenant_role`. Verify that affected users' next JWT (obtained via token refresh) no longer contains the deleted role or its exclusive permissions. This validates the role deletion cascade and the stateless JWT update behavior.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-2 (role lifecycle), AC-3 (JWT reflects changes)
- Functional Requirements: FR-4, FR-6
- Business Rules: BR-7, BR-5

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- A custom role "Report Viewer" exists in tenant "acme" with permissions: `Report.View`, `Report.Export`.
- Two users are assigned the "Report Viewer" role:
  - `user-x@acme.com` (roles: Employee, Report Viewer)
  - `user-y@acme.com` (roles: Employee, Report Viewer, HR Officer)
- Both users have active JWTs containing the "Report Viewer" role.
- `Report.View` and `Report.Export` are not granted by the Employee or HR Officer roles (they are exclusive to Report Viewer).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Custom role to delete | Report Viewer | Assigned to 2 users |
| Exclusive permissions | Report.View, Report.Export | Only in Report Viewer role |
| Affected user 1 | user-x@acme.com | Roles: Employee + Report Viewer |
| Affected user 2 | user-y@acme.com | Roles: Employee + HR Officer + Report Viewer |
| Report Viewer role ID | {report_viewer_role_id} | Custom role UUID |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT. | JWT issued with Tenant Admin role. |
| 2 | Verify current state: `GET /api/v1/tenant/roles/{report_viewer_role_id}`. | HTTP 200 OK. Role "Report Viewer" exists with `user_count: 2`. |
| 3 | Authenticate as `user-x@acme.com` and obtain a JWT (pre-deletion baseline). | JWT contains `roles: ["Employee", "Report Viewer"]` and `permissions` includes `Report.View`, `Report.Export`. |
| 4 | Send `DELETE /api/v1/tenant/roles/{report_viewer_role_id}` as admin. The API may require confirmation for roles with assigned users. | If confirmation required: HTTP 200 OK with a confirmation prompt or the delete accepts a `confirm: true` parameter. Role is deleted. |
| 5 | Verify role is deleted: `GET /api/v1/tenant/roles/{report_viewer_role_id}`. | HTTP 404 Not Found. The role no longer exists. |
| 6 | Verify role list: `GET /api/v1/tenant/roles`. | "Report Viewer" no longer appears in the list. |
| 7 | Verify user assignments cleared: `GET /api/v1/tenant/users/{user_x_id}`. | User X's roles are `["Employee"]` only. "Report Viewer" has been removed from `user_tenant_role`. |
| 8 | Verify user assignments cleared: `GET /api/v1/tenant/users/{user_y_id}`. | User Y's roles are `["Employee", "HR Officer"]`. "Report Viewer" has been removed. |
| 9 | **Before token refresh**: Send `GET /api/v1/tenant/reports` using user-x's old JWT (from step 3). | May still return HTTP 200 OK (JWT is stateless within its lifetime -- the old token still has the permission claims). This is expected behavior per BR-5. |
| 10 | Trigger token refresh for `user-x@acme.com` via `POST /api/v1/auth/refresh`. | New JWT issued. |
| 11 | Decode user-x's refreshed JWT. | `roles` claim is `["Employee"]`. `permissions` no longer includes `Report.View` or `Report.Export`. |
| 12 | Send `GET /api/v1/tenant/reports` using user-x's refreshed JWT. | HTTP 403 Forbidden. User no longer has `Report.View` permission. |
| 13 | Verify audit log entries for the deletion. | Audit log records: role deleted (Report Viewer), role removed from user-x, role removed from user-y, admin who performed the deletion. |

## 6. Postconditions
- The "Report Viewer" custom role no longer exists in tenant "acme".
- All `user_tenant_role` records referencing the deleted role are removed.
- Affected users' refreshed JWTs no longer contain the deleted role or its permissions.
- Audit trail is complete for the deletion and cascade operations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
