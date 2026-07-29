---
id: TC-AUTH-122
user_story: US-AUTH-012
module: Authentication
priority: high
type: security
status: blocked
created: 2026-07-24
---

# TC-AUTH-122: Authorization -- only tenant-admin roles may view or change SSO settings; regular users and managers cannot

## 1. Test Objective
Verify BR-4: SSO settings are viewable and editable only by tenant-admin roles. A regular employee and a (non-admin) manager in the same tenant are denied both read and write of the SSO configuration -- the UI hides/disables the card and the API returns 403 Forbidden. Authorization is enforced server-side, not merely by hiding the UI.

## 2. Related Requirements
- User Story: US-AUTH-012
- Business Rules: BR-4
- Functional Requirements: FR-2
- Dependency: US-AUTH-006 (RBAC)

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`.
- `emp@acme.com` holds only the "Employee" role; `mgr@acme.com` holds a non-admin "Manager" role. Neither has a tenant-admin role.
- SSO settings exist for acme (from TC-AUTH-115).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Non-admin user 1 | emp@acme.com | Employee role |
| Non-admin user 2 | mgr@acme.com | Manager role (non-admin) |
| Read endpoint | GET /api/v1/tenant/auth-settings (SSO fields) | |
| Write endpoint | PUT /api/v1/tenant/auth-settings | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `emp@acme.com`; navigate to Security settings. | The SSO card is not shown (or shown disabled with no edit affordance) -- the employee cannot view SSO config. |
| 2 | As `emp@acme.com`, call `GET /api/v1/tenant/auth-settings` and inspect the SSO fields. | HTTP 403 Forbidden (or the SSO fields are omitted from an allowed base response) -- no `tid`/domain/role leak to a non-admin. |
| 3 | As `emp@acme.com`, call `PUT /api/v1/tenant/auth-settings` attempting to change SSO fields. | HTTP 403 Forbidden; no change persists. |
| 4 | Repeat steps 2-3 as `mgr@acme.com` (non-admin manager). | HTTP 403 Forbidden on both read and write of SSO settings. |
| 5 | Confirm no `sso_config_updated` audit event was written by the non-admin attempts. | No audit record; settings unchanged. |

## 6. Postconditions
- Only tenant admins can read/write SSO settings; managers and employees are denied at the API layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
