---
id: TC-AUTH-116
user_story: US-AUTH-012
module: Authentication
priority: high
type: security
status: pass
created: 2026-07-24
---

# TC-AUTH-116: SSO settings gated by plan entitlement -- section hidden/disabled and write API returns 403 `sso_not_entitled`

## 1. Test Objective
Verify the entitlement gate (AC-2, FR-3): when the tenant's plan has `PlanFeatureFlags.Sso = false`, the SSO card is hidden or shown disabled with an "upgrade your plan" note, and any API attempt to write SSO settings (e.g. enable SSO) is rejected with HTTP 403 and error code `sso_not_entitled` -- even if the request is otherwise well-formed. The gate is server-enforced, not UI-only.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "basicco" (basicco.yourhrm.com) is active and its plan exposes `PlanFeatureFlags.Sso = false`.
- `admin-b@basicco.com` holds a tenant-admin role in basicco.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint (write) | PUT /api/v1/tenant/auth-settings | SSO fields present in payload |
| allowed_entra_tenant_ids | ["7c9e6679-7425-40de-944b-e07fc1f90ae7"] | Valid, but irrelevant -- gate fires first |
| sso_enabled | true | Attempt to enable |
| Expected status | 403 | `sso_not_entitled` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin-b@basicco.com`; open Security settings. | The SSO card is hidden OR rendered disabled with an "Available on higher plans" badge/note; the enable toggle and fields are non-interactive (AC-2 UI). |
| 2 | Bypass the UI: send `PUT /api/v1/tenant/auth-settings` with valid SSO fields (`sso_enabled = true`, valid `tid`). | HTTP 403 Forbidden with error code `sso_not_entitled`. |
| 3 | Re-read `GET /api/v1/tenant/auth-settings`. | SSO fields remain at defaults (`sso_enabled = false`, empty allow-list) -- the rejected write persisted nothing. |
| 4 | Confirm no `sso_config_updated` audit event was written for basicco. | No SSO audit record exists (the write never succeeded). |

## 6. Postconditions
- basicco's SSO settings are unchanged; no partial write occurred.
- The entitlement gate is proven server-side, independent of the UI.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
