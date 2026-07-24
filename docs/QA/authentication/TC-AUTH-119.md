---
id: TC-AUTH-119
user_story: US-AUTH-012
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-119: `jit_default_role` privilege-escalation guard -- privileged/admin roles rejected

## 1. Test Objective
Verify AC-5 / FR-6 / BR-5: the `jit_default_role` for JIT-provisioned users must (1) exist in the tenant's role set (US-AUTH-006) and (2) not be a privileged role above the allowed ceiling (system-admin or tenant-owner). Setting it to a privileged role, or to a role that does not exist in the tenant, is rejected -- preventing JIT users from being auto-granted admin privileges.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.
- acme's role set includes built-in "Tenant Owner" and "Tenant Admin" (privileged) and "Employee" (non-privileged).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| privileged role | Tenant Owner | Above ceiling -> reject (BR-5) |
| privileged role 2 | Tenant Admin | Above ceiling -> reject |
| non-existent role | Ghost Role | Not in acme roles -> reject (FR-6) |
| valid role | Employee | Non-privileged, exists -> accept |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the SSO card, open the default-role dropdown. | The dropdown is filtered to non-privileged roles; "Tenant Owner"/"Tenant Admin" are not selectable (UI guard). |
| 2 | Bypass the UI: `PUT /api/v1/tenant/auth-settings` with `jit_default_role = "Tenant Owner"`. | HTTP 400 Bad Request; validation error that the role exceeds the maximum-privilege ceiling. |
| 3 | `PUT` with `jit_default_role = "Tenant Admin"`. | HTTP 400 Bad Request -- same privilege-ceiling rejection. |
| 4 | `PUT` with `jit_default_role = "Ghost Role"` (not in acme's role set). | HTTP 400 Bad Request; validation error that the role does not exist in this tenant. |
| 5 | `PUT` with `jit_default_role = "Employee"` (valid, plus a non-empty allow-list). | HTTP 200 OK; the non-privileged role persists. |
| 6 | Confirm none of the rejected attempts wrote a `sso_config_updated` audit event or persisted a privileged role. | No privileged `jit_default_role` is ever stored. |

## 6. Postconditions
- `jit_default_role` can only ever be a tenant-existing, non-privileged role.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
