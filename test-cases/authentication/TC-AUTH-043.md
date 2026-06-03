---
id: TC-AUTH-043
user_story: US-AUTH-006
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-043: Permission union when user has two overlapping roles

## 1. Test Objective
Verify that when a user is assigned multiple roles with overlapping permissions, the effective permissions are the union of all role permissions. No permission should be duplicated, lost, or conflicted. The JWT claims must reflect the complete union set.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-3, AC-4
- Functional Requirements: FR-1, FR-3, FR-4
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- Two custom roles exist in tenant "acme":
  - "Leave Manager" with permissions: `Leave.View`, `Leave.Approve.Team`, `Leave.Configure`
  - "Attendance Manager" with permissions: `Leave.View`, `Attendance.View`, `Attendance.Manage`
- Note: `Leave.View` is shared between both roles (the overlap).
- User `multi-role@acme.com` exists in tenant "acme" with only the `Employee` role initially.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Target user | multi-role@acme.com | Will receive two custom roles |
| Role A | Leave Manager | Permissions: Leave.View, Leave.Approve.Team, Leave.Configure |
| Role B | Attendance Manager | Permissions: Leave.View, Attendance.View, Attendance.Manage |
| Overlapping permission | Leave.View | Present in both roles |
| Expected union | Leave.View, Leave.Approve.Team, Leave.Configure, Attendance.View, Attendance.Manage + Employee perms | 5 unique custom permissions + Employee baseline |
| Endpoint requiring Leave.Configure | PUT /api/v1/tenant/leave/policies | Only in Role A |
| Endpoint requiring Attendance.Manage | POST /api/v1/tenant/attendance/adjustments | Only in Role B |
| Endpoint requiring Payroll.View | GET /api/v1/tenant/payroll/runs | In neither role |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT. | JWT issued with Tenant Admin role. |
| 2 | Assign both "Leave Manager" and "Attendance Manager" roles to `multi-role@acme.com` via `PATCH /api/v1/tenant/users/{user_id}` with `{ "roleIds": ["{employee_id}", "{leave_mgr_id}", "{attendance_mgr_id}"] }`. | HTTP 200 OK. User's `user_tenant_role` records updated. |
| 3 | Authenticate as `multi-role@acme.com` (or refresh token) to obtain a fresh JWT. | New JWT issued. |
| 4 | Decode the JWT and inspect the `roles` claim. | `roles` contains `["Employee", "Leave Manager", "Attendance Manager"]`. |
| 5 | Decode the JWT and inspect the `permissions` claim. | `permissions` includes `Leave.View`, `Leave.Approve.Team`, `Leave.Configure`, `Attendance.View`, `Attendance.Manage` (plus Employee-level permissions). `Leave.View` appears exactly once (no duplicates). |
| 6 | Send `PUT /api/v1/tenant/leave/policies` (requires Leave.Configure, from Role A only). | HTTP 200 OK. Access granted via Leave Manager role. |
| 7 | Send `POST /api/v1/tenant/attendance/adjustments` (requires Attendance.Manage, from Role B only). | HTTP 200 OK. Access granted via Attendance Manager role. |
| 8 | Send `GET /api/v1/tenant/payroll/runs` (requires Payroll.View, in neither role). | HTTP 403 Forbidden. Permission not in the union set. |
| 9 | Verify via `GET /api/v1/tenant/users/{user_id}` that the API returns the effective permission list as a deduplicated union. | User profile/detail endpoint shows the full permission set without duplicates. |

## 6. Postconditions
- User `multi-role@acme.com` has three roles (Employee, Leave Manager, Attendance Manager).
- Effective permissions are the exact union of all three roles' permissions with no duplicates.
- Authorization correctly grants access for any permission in the union and denies access for permissions outside it.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
