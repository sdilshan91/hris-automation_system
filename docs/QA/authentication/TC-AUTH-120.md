---
id: TC-AUTH-120
user_story: US-AUTH-012
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-120: `enforcement_mode = sso_only` blocked without a preserved break-glass path; accepted once precondition met

## 1. Test Objective
Verify AC-7 / FR-5 / BR-6: setting `enforcement_mode` to `sso_only` is accepted **only if** a break-glass local-admin path is preserved (US-AUTH-016). Without the break-glass precondition, the save warns and is blocked, so a tenant cannot lock itself out. Once the precondition is satisfied, the same save succeeds. Switching to `sso_only` governs new logins only and does not retroactively invalidate existing local-login sessions (BR-6).

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-7
- Functional Requirements: FR-5
- Business Rules: BR-6
- Dependency: US-AUTH-016 (break-glass)

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.
- SSO is enabled with a valid allow-list (from TC-AUTH-115), currently `enforcement_mode = optional`.
- Initially, no break-glass admin path is configured for acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| enforcement_mode | sso_only | Target |
| break-glass precondition | absent -> present | US-AUTH-016 precondition |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With no break-glass path configured, in the SSO card select enforcement = "SSO only". | An inline warning about break-glass appears; the selector/submit is blocked while the precondition is unmet (AC-7 UI). |
| 2 | Bypass the UI: `PUT /api/v1/tenant/auth-settings` with `enforcement_mode = "sso_only"`. | HTTP 400/409 (blocked); error indicates the break-glass precondition (US-AUTH-016) is not met. |
| 3 | Re-read settings. | `enforcement_mode` is still `optional` -- the blocked write persisted nothing. |
| 4 | Satisfy the break-glass precondition (configure the local-admin break-glass path per US-AUTH-016). | Precondition now met. |
| 5 | Resubmit `PUT` with `enforcement_mode = "sso_only"`. | HTTP 200 OK; `enforcement_mode` persists as `sso_only`; an `sso_config_updated` audit event records the enforcement change (before/after). |
| 6 | Confirm an existing local-login session (established before the change) is still valid; only NEW logins are governed by `sso_only`. | The pre-existing session is not retroactively invalidated (BR-6). |

## 6. Postconditions
- `sso_only` can only be set when a break-glass path exists; existing sessions survive the switch.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
