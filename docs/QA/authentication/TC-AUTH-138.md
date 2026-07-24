---
id: TC-AUTH-138
user_story: US-AUTH-011
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-138: Valid callback completes the OIDC round-trip and terminates in the application's own JWT

## 1. Test Objective
Verify AC-2 / AC-3 / AC-4 (the happy-path callback): a callback carrying a valid authorization `code` + signed `state` (a) validates `state` and resolves the originating HRM tenant FROM it before any token exchange, (b) exchanges the code server-side and fully validates the returned `id_token` (JWKS signature, `aud == ClientId`, per-`tid` issuer, `exp`, and `nonce`), and (c) on success issues the application's own JWT + refresh token via the existing `JwtService` (identical in shape to a local login), sets the refresh-token cookie, and redirects the browser back to the ORIGINATING tenant subdomain.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-2, AC-3, AC-4
- Functional Requirements: FR-2, FR-3, FR-4, FR-5, FR-6
- Business Rules: BR-1 (SSO terminates in the same app JWT)

## 3. Preconditions
- Entra SSO configured (`IsConfigured == true`); Microsoft token endpoint + JWKS reachable (or a test double returns a validly-signed `id_token`).
- Tenant "acme" has an SSO allow-list entry whose `AllowedTenantIds` (or `AllowedDomains`) permits the test directory `tid` C1 / domain `acme.com` (US-AUTH-013 guard must pass for a successful login).
- A challenge for acme was performed (TC-AUTH-137), producing a valid signed `state` (subdomain=acme, nonce=N, code verifier V) and Microsoft returned an authorization `code`.
- `user-a@acme.com` (Entra `oid`=O1) is a valid member of acme (or JIT-eligible).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Callback endpoint | GET /api/v1/auth/sso/callback?code={valid}&state={signed} | The single fixed redirect URI |
| id_token claims | tid=C1, oid=O1, email=user-a@acme.com, name="User A", nonce=N, aud=ClientId, iss=https://login.microsoftonline.com/C1/v2.0, exp=future | Signed by Microsoft JWKS |
| Allow-list (acme) | AllowedTenantIds=[C1] (and/or AllowedDomains=[acme.com]) | Permits this org |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/auth/sso/callback?code={valid}&state={signed}`. | State is unprotected/validated first; the HRM tenant is resolved to `acme` FROM the state (AC-2) — before the token exchange happens. |
| 2 | Observe the server-side code exchange. | The code is POSTed to the Microsoft token endpoint with `client_id`, `client_secret`, `grant_type=authorization_code`, `code`, the FIXED `redirect_uri`, and the PKCE `code_verifier` from the state (FR-4) — the secret never leaves the server. |
| 3 | Observe id_token validation. | Signature validated against Microsoft JWKS, `aud == ClientId`, issuer matches `https://login.microsoftonline.com/{tid}/v2.0` for the token's own `tid`, `exp` in the future, and `nonce == state.nonce` (AC-3). All pass. |
| 4 | Observe session issuance. | On success `JwtService` mints an app JWT + refresh token identical in shape to a local login (US-AUTH-002); response sets `Set-Cookie: refreshToken` (HttpOnly, Secure, SameSite=Strict, Path=/api/v1/auth). |
| 5 | Observe the final redirect. | HTTP 302 to the ORIGINATING SPA origin + `/auth/sso/callback?returnUrl=%2Fdashboard` (the subdomain that started the flow, from the state), NOT the fixed redirect host. No tokens are placed in the URL. |
| 6 | From the returned session, call an authenticated API (e.g. `GET /api/v1/auth/me`) at acme. | Succeeds and reports the acme tenant + user-a — downstream RBAC/refresh/sessions are unchanged (FR-6). |
| 7 | Check the audit trail. | A success record for the SSO login is written for tenant acme (per AC/FR-8, `sso_login_succeeded` with tenant + `tid`/`oid`, non-sensitive). |

## 6. Postconditions
- user-a has an active acme session created via SSO, indistinguishable downstream from a local login.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
