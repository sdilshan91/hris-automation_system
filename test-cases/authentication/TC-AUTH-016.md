---
id: TC-AUTH-016
user_story: US-AUTH-006
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-016: User with Admin role can access admin endpoints

## 1. Test Objective
Verify that a user with the "Tenant Admin" role and appropriate permissions can access admin-only endpoints, and the authorization middleware correctly evaluates role/permission claims from the JWT.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-3, AC-4
- Functional Requirements: FR-1, FR-4, FR-5

## 3. Preconditions
- User `admin@acme.com` is authenticated in tenant "acme" with the "Tenant Admin" role.
- The JWT contains `roles: ["Tenant Admin"]` and includes admin-level permissions (e.g., `User.Manage`, `Role.Manage`, `Tenant.Settings.Manage`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | admin@acme.com | Tenant Admin |
| Role | Tenant Admin | Built-in admin role |
| Permission (example) | User.Manage | Required for user management |
| Admin endpoint | GET /api/v1/tenant/users | List all tenant users |
| Admin endpoint 2 | POST /api/v1/tenant/roles | Create custom role |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT | JWT contains `roles: ["Tenant Admin"]` and admin permissions in the `permissions` claim. |
| 2 | Send `GET /api/v1/tenant/users` with the JWT in the Authorization header | HTTP 200 OK; list of tenant users is returned. |
| 3 | Send `POST /api/v1/tenant/roles` with `{ name: "Custom Role", description: "Test", permissions: ["Leave.View"] }` | HTTP 201 Created; new custom role is created scoped to this tenant. |
| 4 | Send `GET /api/v1/tenant/roles` | HTTP 200 OK; list includes both built-in roles and the newly created custom role. |
| 5 | Send `PUT /api/v1/tenant/auth-settings` to update session policy | HTTP 200 OK; tenant admin has access to security settings. |
| 6 | Verify all requests pass through the three authorization layers: (1) JWT validation, (2) policy-based permission check, (3) resource-level check | No 401 or 403 errors for authorized operations. |
| 7 | Verify authorization success is achieved with less than 5ms overhead | Permission evaluation from cached JWT claims is fast. |

## 6. Postconditions
- Admin endpoints return successful responses for the Tenant Admin user.
- A custom role has been created in the tenant.
- Authorization audit logs are recorded if configured.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
