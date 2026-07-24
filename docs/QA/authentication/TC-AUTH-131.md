---
id: TC-AUTH-131
user_story: US-AUTH-016
module: Authentication
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-131: SSO onboarding generates the correct Microsoft admin-consent URL for the vendor multi-tenant app

## 1. Test Objective
Verify AC-4 / FR-5: when a customer Microsoft 365 admin opens the SSO onboarding wizard, HRM generates the correct admin-consent URL for the vendor's multi-tenant Entra app and guides them to grant tenant-wide consent. The URL targets the Microsoft admin-consent endpoint with the vendor `ClientId`, the fixed redirect, and (per BR-3/US-AUTH-013) a signed single-use `state` that carries the resolved HRM tenant subdomain -- not derived from any token `tid`.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5
- Business Rules: BR-3 (state carries tenant; `tid` captured only on return)

## 3. Preconditions
- Tenant "acme" is active with `PlanFeatureFlags.Sso = true`; `sso_onboarding_status = not_started` (or `consent_pending`).
- The vendor multi-tenant Entra app (CR-AUTH-001 §4) is registered; its `ClientId` and the single fixed redirect (`app.yourhrm.com/signin-oidc` or the onboarding-consent return) are configured.
- `admin-a@acme.com` is a tenant admin opening the onboarding wizard.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Consent-URL endpoint | GET /api/v1/tenant/sso/admin-consent-url | Onboarding wizard "Grant admin consent" |
| Expected host/path | https://login.microsoftonline.com/organizations/v2.0/adminconsent (or /common/adminconsent) | Admin-consent endpoint |
| client_id | vendor multi-tenant app ClientId | From config, not per-tenant |
| redirect_uri | the single fixed redirect | Wildcard subdomain redirects unreliable in Entra |
| state | signed, single-use, carries subdomain "acme" | Never carries a secret; not from token `tid` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `admin-a@acme.com`, open onboarding step 1 ("Grant admin consent"); `GET /api/v1/tenant/sso/admin-consent-url`. | HTTP 200 returns an admin-consent URL. `sso_onboarding_status` transitions to `consent_pending`. |
| 2 | Parse the returned URL. | Host is the Microsoft admin-consent endpoint; `client_id` equals the vendor multi-tenant app ClientId; `redirect_uri` equals the single fixed redirect exactly (no wildcard subdomain). |
| 3 | Inspect the `state` parameter. | `state` is present, signed, single-use, and encodes the resolved HRM tenant ("acme") -- so the return can bind to acme WITHOUT trusting the token's `tid`. No client secret or cert material appears in the URL. |
| 4 | Request the consent URL again for a second tenant "globex". | The URL differs only where it must (its own signed `state` for globex); the `client_id` is the SAME shared vendor app (one multi-tenant app, not one-app-per-customer). |

## 6. Postconditions
- A correct, tenant-bound admin-consent URL is available to the customer admin; onboarding status is `consent_pending`.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
