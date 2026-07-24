---
id: TC-AUTH-132
user_story: US-AUTH-016
module: Authentication
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-132: Successful admin consent captures the customer `tid` into the allow-list, advances onboarding status, and audits `sso_admin_consent_completed` -- but does NOT auto-enable SSO

## 1. Test Objective
Verify AC-5 / FR-6 / BR-3: after the customer admin grants Microsoft admin consent and returns to HRM, the system captures the customer's Entra Directory ID (`tid`) into the resolved tenant's allow-list (US-AUTH-012), advances `sso_onboarding_status` to `consented` (SSO "ready to enable"), and audits `sso_admin_consent_completed`. Consent alone does NOT enable SSO -- `sso_enabled` remains false and `enforcement_mode` is unchanged until the admin explicitly enables it (BR-3). The tenant binding comes from the signed `state`, and `tid` is captured only for the resolved tenant.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-7
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" completed TC-AUTH-131 (`sso_onboarding_status = consent_pending`), a valid signed single-use `state` for acme is outstanding.
- `sso_enabled = false`, `enforcement_mode = optional` for acme before the return.
- Microsoft returns a successful `admin_consent=True` with the customer directory `tid = C1`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Consent return | GET {fixed-redirect}?admin_consent=True&tenant=C1&state={signed} | Consent callback |
| Customer tid (C1) | cccccccc-cccc-cccc-cccc-cccccccccccc | Captured into acme allow-list |
| Audit action | `sso_admin_consent_completed` | US-NTF-004 |
| sso_onboarding_status (after) | `consented` | Ready to enable, not enabled |
| sso_enabled (after) | false | Unchanged -- explicit enable still required (BR-3) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Complete Microsoft admin consent and follow the return to the fixed redirect with `admin_consent=True`, `tid = C1`, and the valid signed `state`. | The return is accepted; `state` validates (signed, single-use, resolves to acme). HTTP 200 / redirect back into the wizard. |
| 2 | Re-read `GET /api/v1/tenant/auth-settings` for acme. | `C1` now appears in acme's allowed Entra `tid` allow-list; `sso_onboarding_status = consented`. |
| 3 | Check `sso_enabled` and `enforcement_mode`. | `sso_enabled` is STILL false and `enforcement_mode` STILL `optional` -- consent did not auto-enable SSO (BR-3). |
| 4 | Inspect the audit log. | Exactly one `sso_admin_consent_completed` record for acme + admin-a, capturing the directory `tid` (no secret material). |
| 5 | Replay the same consent-return URL (same `state`). | Rejected -- `state` is single-use; no duplicate `tid` capture, no duplicate audit. |
| 6 | Complete the wizard's explicit "Enable SSO" step. | Only now does `sso_enabled` become true (via the US-AUTH-012 write) -- confirming enablement is a separate deliberate action. |

## 6. Postconditions
- acme's allow-list contains `C1`; onboarding is `consented`; SSO is ready but not yet enabled until the admin acts.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
