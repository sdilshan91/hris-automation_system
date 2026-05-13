---
id: TC-AUTH-018
user_story: US-AUTH-006
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-018: Roles are tenant-scoped

## 1. Test Objective
Verify that a user who belongs to multiple tenants has different roles in each tenant, and that the JWT issued for each tenant contains only that tenant's roles and permissions with no cross-tenant leakage.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-7
- Functional Requirements: FR-2, FR-4, FR-10
- Business Rules: BR-1

## 3. Preconditions
- User `multi@acme.com` has active memberships in two tenants:
  - Tenant A ("acme"): role = "Tenant Admin"
  - Tenant B ("globex"): role = "Employee"
- Both tenants are in `active` state.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | multi@acme.com | Multi-tenant user |
| Tenant A | acme (acme.yourhrm.com) | Role: Tenant Admin |
| Tenant B | globex (globex.yourhrm.com) | Role: Employee |
| Tenant A permissions | User.Manage, Role.Manage, ... | Full admin permissions |
| Tenant B permissions | Profile.View, Leave.Request, ... | Employee-level only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate at `acme.yourhrm.com` with `multi@acme.com` | JWT issued with `tenant_id` = acme tenant UUID. |
| 2 | Decode the JWT and verify roles claim | `roles: ["Tenant Admin"]` -- only acme roles. |
| 3 | Verify permissions claim contains only acme admin permissions | Permissions include `User.Manage`, `Role.Manage`, etc. |
| 4 | Verify no globex roles or permissions appear in the JWT | No "Employee" role or employee-only permissions from globex. |
| 5 | Access admin endpoint `GET /api/v1/tenant/users` at acme | HTTP 200 OK; admin access granted in acme. |
| 6 | Authenticate at `globex.yourhrm.com` with `multi@acme.com` | JWT issued with `tenant_id` = globex tenant UUID. |
| 7 | Decode the JWT and verify roles claim | `roles: ["Employee"]` -- only globex roles. |
| 8 | Verify permissions claim contains only globex employee permissions | No admin permissions present. |
| 9 | Verify no acme roles or permissions appear in the JWT | No "Tenant Admin" role or admin permissions from acme. |
| 10 | Access admin endpoint `GET /api/v1/tenant/users` at globex | HTTP 403 Forbidden; user is only Employee in globex. |
| 11 | Verify that querying roles via `GET /api/v1/tenant/roles` returns only the current tenant's roles | Acme shows acme roles; globex shows globex roles. No cross-tenant role data. |

## 6. Postconditions
- Each tenant JWT contains only the roles and permissions for that specific tenant.
- Authorization decisions are correctly tenant-scoped.
- No cross-tenant data leakage in role/permission claims.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
