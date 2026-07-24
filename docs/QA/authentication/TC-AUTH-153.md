---
id: TC-AUTH-153
user_story: US-AUTH-011
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-153: When SSO is not configured, both challenge and callback fail closed (`not_configured`) with no token

## 1. Test Objective
Verify the fail-closed configuration gate (Preconditions / BR-5): `EntraSsoOptions.IsConfigured` is false unless Enabled + ClientId + ClientSecret + RedirectUri are all present. When SSO is not configured, the challenge cannot start a flow and the callback cannot complete one — both return `not_configured` and issue no token. This keeps the foundation safely off until it is fully provisioned (and, per BR-5, until US-AUTH-013 isolation is enforced in production).

## 2. Related Requirements
- User Story: US-AUTH-011
- Business Rules: BR-5 (disabled until isolation enforced)
- Non-Functional Requirements: NFR-2 (secret from user-secrets)

## 3. Preconditions
- A deployment where SSO is NOT fully configured — e.g. `Enabled=false`, OR ClientSecret blank, OR RedirectUri blank (any one makes `IsConfigured == false`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Challenge | GET /api/v1/auth/sso/challenge?tenant=acme | |
| Callback | GET /api/v1/auth/sso/callback?code=x&state=y | |
| Config gap | ClientSecret empty (one of the required fields) | `IsConfigured == false` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With SSO not configured, `GET /api/v1/auth/sso/challenge?tenant=acme`. | Result `not_configured`; HTTP 302 to `/auth/login?sso_error=not_configured`. NO redirect to Microsoft. |
| 2 | `GET /api/v1/auth/sso/callback?code=x&state=y`. | Result `not_configured`; redirect to login. NO code exchange, NO token. |
| 3 | Provision the missing secret (via user-secrets) so `IsConfigured` becomes true; retry the challenge. | Now 302 to Microsoft with a signed state — confirming the gate, not another failure, blocked steps 1-2. |
| 4 | Confirm the "not configured" state is distinct from "tenant required" (TC-AUTH-152). | With a tenant supplied but SSO unconfigured, the code is `not_configured`, not `tenant_required`. |

## 6. Postconditions
- SSO is inert until fully configured; no flow can start or complete while unconfigured.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
