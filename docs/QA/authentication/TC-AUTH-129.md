---
id: TC-AUTH-129
user_story: US-AUTH-016
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-129: Enabling `sso_only` is blocked when no break-glass admin is designated (or SSO config is incomplete/untested), with a clear explanation

## 1. Test Objective
Verify AC-3 / FR-3 / BR-1: the system refuses to persist `enforcement_mode = sso_only` unless at least one break-glass administrator is designated (and SSO config is complete). The rejection returns a clear, actionable explanation requiring a break-glass admin (and recommending a verified successful SSO test login) before enforcement. This guarantees a tenant can never enforce itself into a locked-out state.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is on `enforcement_mode = optional` with `PlanFeatureFlags.Sso = true`.
- `break_glass_admin_user_ids` is EMPTY (no designated break-glass admin).
- `admin-a@acme.com` is a tenant admin permitted to change enforcement.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Write endpoint | PUT /api/v1/tenant/auth-settings | Enforcement change |
| Attempted change | `enforcement_mode = sso_only`, `break_glass_admin_user_ids = []` | The blocked case |
| Expected error | 422/400 domain error, code e.g. `break_glass_required` | Clear explanation |
| Prior mode | `optional` | Must remain intact after rejection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `admin-a@acme.com`, `PUT /api/v1/tenant/auth-settings` setting `enforcement_mode = sso_only` while `break_glass_admin_user_ids` is empty. | Request REJECTED (HTTP 422/400). Response explains a break-glass admin must be designated first and recommends a verified SSO test login. No change persisted. |
| 2 | Re-read `GET /api/v1/tenant/auth-settings`. | `enforcement_mode` is still `optional`; the tenant is unchanged (fail-closed, BR-1). |
| 3 | In the FE Security > SSO > Enforcement sub-section, attempt to select "SSO-only" and confirm the guarded dialog. | The confirmation is blocked with the same explanation; the SSO-only option cannot be committed until a break-glass admin picker value is provided. |
| 4 | Designate `admin-a@acme.com` (local creds) as break-glass, then re-submit `sso_only`. | The change is now ACCEPTED (HTTP 200); `enforcement_mode = sso_only` persists -- confirming the block was specifically the missing break-glass precondition. |
| 5 | (Config-incomplete variant) With a break-glass admin present but the SSO allow-list empty/SSO disabled, attempt `sso_only`. | Rejected with a clear "complete/verify SSO config first" explanation; prior mode intact. |

## 6. Postconditions
- `sso_only` is only persisted once BOTH the break-glass precondition and a complete SSO config are satisfied.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
