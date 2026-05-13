---
id: TC-AUTH-022
user_story: US-AUTH-008
module: Authentication
priority: high
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-022: User switches tenant without re-auth

## 1. Test Objective
Verify that a cross-tenant user can switch from one tenant to another without re-entering credentials, receiving a new JWT with the target tenant's context, roles, and permissions.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-1, AC-2, AC-5
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-5, FR-6, FR-7, FR-8

## 3. Preconditions
- User `multi@acme.com` is authenticated in tenant "acme" (Tenant A) with the "Manager" role.
- The user has an active membership in tenant "globex" (Tenant B) with the "Employee" role.
- Both tenants are in `active` state.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | multi@acme.com | Multi-tenant user |
| Tenant A | acme (acme.yourhrm.com) | Current tenant, role: Manager |
| Tenant B | globex (globex.yourhrm.com) | Target tenant, role: Employee |
| My-tenants endpoint | GET /api/v1/auth/my-tenants | List all memberships |
| Switch endpoint | POST /api/v1/auth/switch-tenant | Accept { tenantId } |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/auth/my-tenants` while authenticated in tenant A | HTTP 200 with a list of all tenant memberships including acme (current, Manager) and globex (Employee), with logos, status, and roles. |
| 2 | Verify the current tenant is marked with `isCurrentTenant: true` | Acme entry has the flag set. |
| 3 | Send `POST /api/v1/auth/switch-tenant` with `{ tenantId: "<globex-tenant-id>" }` | HTTP 200 with `{ accessToken, tenant: { tenantId, subdomain: "globex", name: "Globex" }, redirectUrl: "https://globex.yourhrm.com/dashboard" }`. |
| 4 | Decode the new JWT and verify `tenant_id` claim | Contains globex tenant UUID, not acme. |
| 5 | Verify `roles` claim contains only globex roles | `roles: ["Employee"]` -- no "Manager" from acme. |
| 6 | Verify `permissions` claim contains only globex employee permissions | No manager-level permissions. |
| 7 | Verify a new refresh token cookie is set for globex | Cookie with `httpOnly; Secure; SameSite=Strict`. |
| 8 | Verify the acme refresh token remains valid | The original session in acme is not revoked. |
| 9 | Navigate to `https://acme.yourhrm.com/dashboard` | Acme session still works; user can access acme as Manager. |
| 10 | Verify `tenant_switch` audit events are logged in both source (acme) and target (globex) tenant logs | Both logs contain source_tenant_id, target_tenant_id, user_id, IP, user_agent. |
| 11 | Verify the browser is redirected to `https://globex.yourhrm.com/dashboard` | Frontend handles the subdomain change seamlessly. |

## 6. Postconditions
- User has a new valid JWT for the globex tenant with Employee role.
- User's session in acme tenant remains active and independent.
- Audit events recorded in both tenants.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
