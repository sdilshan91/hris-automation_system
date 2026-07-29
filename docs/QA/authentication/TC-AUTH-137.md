---
id: TC-AUTH-137
user_story: US-AUTH-011
module: Authentication
priority: critical
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-137: SSO challenge builds a correct, signed, single-use redirect to Microsoft Entra (`organizations` authority)

## 1. Test Objective
Verify AC-1 / FR-1 / FR-3: hitting the SSO challenge endpoint for a resolved tenant returns a 302 redirect to the Microsoft `organizations` authorize endpoint carrying `client_id`, `response_type=code`, the FIXED `redirect_uri`, the configured `scope`, a `nonce`, and a tamper-evident, time-limited signed `state` that carries the HRM tenant subdomain. (Implementation reality: the redirect target is the `authorization_endpoint` from the Entra discovery document for `https://login.microsoftonline.com/organizations/v2.0`, and the URL additionally carries PKCE `code_challenge` + `code_challenge_method=S256`, `response_mode=query`, and `prompt=select_account` — assert those too.)

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-3
- Non-Functional Requirements: NFR-3 (state/nonce cryptographically random, single-use)
- Business Rules: BR-2 (authority is always `/organizations`), BR-4 (one fixed redirect host)

## 3. Preconditions
- Entra SSO is configured for the deployment: `EntraSsoOptions.IsConfigured == true` (Enabled + ClientId + ClientSecret + RedirectUri all present, sourced from user-secrets).
- Tenant "acme" (acme.yourhrm.com) is active and resolvable (US-AUTH-007).
- The Entra OIDC discovery document is reachable (or cached) so the authorize endpoint can be resolved.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Challenge endpoint | GET /api/v1/auth/sso/challenge?tenant=acme&returnUrl=/dashboard | Full-page browser redirect (no bearer/tenant header) |
| Configured ClientId | 11111111-1111-1111-1111-111111111111 | Vendor multi-tenant app |
| Configured RedirectUri | https://app.yourhrm.com/signin-oidc (dev: http://localhost:5000/api/v1/auth/sso/callback) | The single fixed redirect |
| Configured Authority | https://login.microsoftonline.com/organizations/v2.0 | `organizations` only |
| Configured Scopes | openid profile email | From `EntraSsoOptions.Scopes` (assert the deployed value) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/auth/sso/challenge?tenant=acme&returnUrl=/dashboard`. | HTTP 302. `Location` host is `login.microsoftonline.com` and the path is the `/organizations/.../authorize` endpoint from discovery — NEVER `/consumers` or `/common` (BR-2). |
| 2 | Parse the `Location` query string. | Contains `client_id`=configured ClientId, `response_type=code`, `redirect_uri`=the FIXED configured RedirectUri (BR-4), `scope`=configured `Scopes`, `response_mode=query`, `prompt=select_account`, a non-empty `nonce`, `code_challenge` with `code_challenge_method=S256`, and a non-empty `state`. |
| 3 | Inspect the `state` value. | Opaque (Data-Protection-protected) blob, NOT plaintext JSON — the tenant subdomain and nonce are not readable/forgeable. It is bound to a 10-minute lifetime. |
| 4 | Repeat the challenge a second time. | A fresh `state`, `nonce`, and `code_challenge` are generated each call (cryptographically random, NFR-3) — no reuse across challenges. |
| 5 | Confirm no application JWT/refresh token is issued at the challenge step. | No `Set-Cookie: refreshToken` and no token in the redirect — the challenge only starts the flow. |

## 6. Postconditions
- The browser is redirected to Microsoft with a valid signed `state`; no session is established yet.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
