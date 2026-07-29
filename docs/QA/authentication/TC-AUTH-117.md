---
id: TC-AUTH-117
user_story: US-AUTH-012
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-117: Enabling SSO with an empty allow-list is rejected (fail-closed) -- `sso_enabled` stays false

## 1. Test Objective
Verify the fail-closed allow-list rule (AC-4, FR-5, BR-3): an entitled tenant admin cannot enable SSO when both `allowed_entra_tenant_ids` and `allowed_email_domains` are empty. The save is rejected with a validation error and `sso_enabled` remains false. Without a trusted directory or domain, the `organizations` authority would authenticate any Entra user, so an empty allow-list must never enable SSO.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.
- acme's SSO is currently disabled with an empty allow-list.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| allowed_entra_tenant_ids | [] | Empty |
| allowed_email_domains | [] | Empty |
| sso_enabled | true | Attempt to enable with no allow-list |
| Expected error | validation error | "Add at least one trusted directory or email domain before enabling SSO" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the SSO card, leave both the `tid` and domain lists empty and attempt to toggle Enable SSO on. | The Enable-SSO toggle is blocked with a tooltip ("Add at least one trusted directory or email domain") -- inline, before submit (AC-4 UI). |
| 2 | Bypass the UI: send `PUT /api/v1/tenant/auth-settings` with `sso_enabled = true`, empty `tid` and domain lists. | HTTP 400 Bad Request with a validation error message about the empty allow-list. |
| 3 | Re-read `GET /api/v1/tenant/auth-settings`. | `sso_enabled` is still `false`; allow-list still empty -- nothing persisted (fail-closed). |
| 4 | Confirm no `sso_config_updated` audit event was written. | No SSO audit record for this rejected attempt. |

## 6. Postconditions
- SSO remains disabled for acme; the fail-closed guard held at both UI and API layers.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
