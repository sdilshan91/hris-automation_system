---
id: US-AUTH-011
module: Authentication & Authorization
priority: Should Have
persona: Tenant User (all roles)
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-011: Entra OIDC authentication foundation

## 1. Description
**As a** tenant user whose organization uses Microsoft 365,
**I want to** authenticate to the HRM platform by signing in with my Microsoft Entra ID account,
**So that** I can access the application using my existing corporate identity without a separate HRM password.

**As a** platform engineer,
**I want** the OIDC challenge → Microsoft → callback → `id_token` validation → app-JWT issuance flow implemented as the foundation,
**So that** per-tenant configuration, isolation, and user matching (US-AUTH-012/013/014) can build on a working federated front door.

## 2. Preconditions
- A vendor-owned **multi-tenant** Entra app registration exists (`signInAudience = AzureADMultipleOrgs`) with the fixed redirect URI `https://app.yourhrm.com/signin-oidc` registered (CR-AUTH-001 §4).
- Platform OIDC configuration is present (ClientId, authority `https://login.microsoftonline.com/organizations`, client secret/cert) supplied via secrets, never committed.
- The resolved HRM tenant (US-AUTH-007) is known when the challenge is initiated.
- The existing app JWT issuance (US-AUTH-002 / `JwtService`) is available to mint tokens after a successful federated login.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant context is resolved and SSO is available | The browser hits the SSO challenge endpoint (`/auth/sso/challenge`) with the tenant | The system builds a signed, single-use `state` carrying the HRM tenant + a `nonce`, and returns a 302 redirect to `https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize` with `client_id`, `response_type=code`, the fixed `redirect_uri`, scope `openid profile email`, `state`, and `nonce`. |
<!-- ISSUE-329 (resolved 2026-07-25): scope amended from "openid profile email User.Read" to "openid profile email" to match the shipped EntraSsoOptions.Scopes default. The flow uses id_token claims only — nothing calls Microsoft Graph — so the User.Read Graph scope was dead. Re-add User.Read only if a Graph profile/photo call is later introduced. -->

| AC-2 | The user completes authentication at Microsoft | Microsoft redirects back to the fixed `/signin-oidc` callback with `code` + `state` | The system validates `state` (signature, single-use, not expired) and resolves the originating HRM tenant from it before any token exchange. |
| AC-3 | A valid authorization `code` and `state` are received | The callback exchanges the code at the Microsoft token endpoint | The system retrieves the `id_token`, validates signature against Microsoft JWKS, and validates `aud == ClientId`, `iss`, `exp`, and the `nonce`. |
| AC-4 | The `id_token` is fully validated | Token validation succeeds | The system issues the application's own JWT + refresh token via the existing `JwtService` (US-AUTH-002), identical in shape to a local login, and redirects the browser back to the originating tenant subdomain. |
| AC-5 | The `state` is tampered with, missing, expired, or replayed | The callback processes it | The system rejects the request with a generic auth error, issues **no** token, and logs an `sso_state_invalid` audit event. |
| AC-6 | The `id_token` fails any validation check (bad signature, wrong audience, expired, nonce mismatch) | The callback processes it | The system rejects the login, issues **no** token, and logs an `sso_token_validation_failed` audit event (with the failure reason, not the raw token). |
| AC-7 | Microsoft returns an OIDC error to the callback (e.g. `access_denied`, user cancelled) | The callback processes the error response | The system does **not** issue a token, does **not** increment the local lockout counter (US-AUTH-010 BR-6), and returns the user to the login page with a friendly message. |

## 4. Functional Requirements
- FR-1: The system SHALL expose an SSO **challenge** endpoint that redirects to Microsoft Entra using the Authorization Code flow against the `organizations` authority.
- FR-2: The system SHALL expose a single fixed **callback** endpoint (`/signin-oidc`) that handles the authorization-code response for all tenants.
- FR-3: The system SHALL carry the resolved HRM tenant in a **tamper-evident, single-use `state`** parameter and resolve the tenant from it on callback.
- FR-4: The system SHALL exchange the authorization code for tokens server-side, keeping the client secret/cert confidential.
- FR-5: The system SHALL fully validate the `id_token` (signature via Microsoft JWKS, `aud`, `iss`, `exp`, `nonce`) before trusting any claim.
- FR-6: On success, the system SHALL issue the application's own JWT + refresh token via the existing `JwtService`, so all downstream flows (RBAC, sessions, refresh, logout) are unchanged.
- FR-7: The system SHALL register the OIDC scheme **in addition to** the existing `AddJwtBearer` scheme; bearer JWT validation for API requests SHALL remain the default and SHALL be unaffected.
- FR-8: All SSO login outcomes (success, state-invalid, token-invalid, IdP error) SHALL be written to the audit log with the resolved tenant, the Entra `tid`/`oid` where available, and the outcome.
- FR-9: The custom issuer/`tid` validation hook SHALL be present as a seam in this story but MAY allow any org for the Phase-1 POC; the enforced allow-listing is delivered by US-AUTH-013 (this story SHALL NOT ship to production without 013).

## 5. Non-Functional Requirements
- NFR-1: Microsoft's JWKS signing keys SHALL be cached and refreshed automatically (per OIDC metadata) rather than fetched per request; key lookup SHALL add <= 5 ms to the callback in the warm case.
- NFR-2: The client secret/certificate SHALL be sourced from secrets (user-secrets / Key Vault), never from committed configuration (Critical Rule #6).
- NFR-3: `state` and `nonce` SHALL be cryptographically random (>= 128 bits) and single-use; replay SHALL be detectable.
- NFR-4: The full callback round-trip (code exchange + validation + JWT issuance) SHALL complete within 2 seconds at P95 under normal Microsoft latency.
- NFR-5: No `id_token`, `access_token`, authorization code, or client secret SHALL ever be written to logs.

## 6. Business Rules
- BR-1: SSO is an **alternative front door** only — it always terminates in the same application JWT; no downstream component distinguishes an SSO-originated session from a local one except via an `auth_method` claim/flag.
- BR-2: The authority is always `https://login.microsoftonline.com/organizations` (work/school accounts), **never** `/consumers` or `/common` — personal Microsoft accounts are not eligible.
- BR-3: IdP errors and user cancellations are **not** credential failures and SHALL NOT count toward account lockout (US-AUTH-010 BR-6).
- BR-4: The fixed redirect host is the only registered redirect URI; per-tenant redirect URIs SHALL NOT be used (CR-AUTH-001 §3).
- BR-5: Until US-AUTH-013 is merged, the foundation SHALL remain disabled in production (feature-flagged off) because tenant isolation is not yet enforced.

## 7. Data Requirements
- **OIDC platform config (app-level, secrets):** `ClientId`, `Authority`, `RedirectUri`, `ClientSecret`/certificate ref, scopes.
- **`state` payload:** HRM tenant id/subdomain, `nonce`, issued-at, expiry, signature/HMAC.
- **Claims consumed from `id_token`:** `tid` (Entra directory id), `oid` (Entra object id), `email`/`preferred_username` (verified email), `name`.
- **Audit records:** `sso_login_succeeded`, `sso_state_invalid`, `sso_token_validation_failed`, `sso_idp_error` (each with tenant, outcome, and non-sensitive context).

## 8. UI/UX Notes
- This story is primarily backend; the user-facing button and callback UX are US-AUTH-015. For the POC, a plain link to the challenge endpoint is acceptable.
- On any failure, the user is returned to the standard login page with a non-technical message ("We couldn't sign you in with Microsoft. Please try again or use your email and password.").
- The browser is returned to the **originating tenant subdomain**, not the fixed redirect host, after the app JWT is issued.

## 9. Dependencies
- US-AUTH-002 (JWT issuance + refresh) — reused to mint tokens post-SSO.
- US-AUTH-007 (tenant resolution from subdomain) — supplies the tenant carried in `state`.
- US-AUTH-013 (tenant `tid`/domain isolation) — **must** ship before this is enabled in production.
- CR-AUTH-001 §4 (vendor Entra app registration) — external prerequisite.
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` / `Microsoft.Identity.Web`.

## 10. Assumptions & Constraints
- The vendor multi-tenant Entra app is already registered and admin-consent onboarding (US-AUTH-016) will follow; for the POC a single test org grants consent manually.
- Cross-subdomain token return is handled by returning tokens via a one-time code rather than a cross-site cookie (see CR-AUTH-001 OQ-1).
- v1 is OIDC/Entra only; the handler is written so a second OIDC IdP could be added later but that is out of scope.
- The existing `AddJwtBearer` API-protection scheme is the default; OIDC is only used on the two SSO endpoints.

## 11. Test Hints
- **Happy path:** Drive challenge → mock/real Microsoft → callback with a valid signed `id_token`; assert an app JWT + refresh token are issued and the redirect targets the originating subdomain.
- **State tampering:** Modify/resend a used `state`; assert rejection + `sso_state_invalid` audit, no token.
- **Token validation:** Feed an `id_token` with wrong `aud`, expired `exp`, bad signature, and mismatched `nonce` (four cases); assert each is rejected with `sso_token_validation_failed`.
- **IdP error:** Simulate `error=access_denied` on the callback; assert no token, lockout counter unchanged, friendly redirect.
- **No-secret-in-logs:** Capture logs across a full flow; assert no token/code/secret material is present.
- **Scheme coexistence:** Assert normal bearer-JWT API requests still authenticate unchanged after the OIDC scheme is registered.
- **JWKS caching:** Assert signing keys are cached and a second callback does not re-fetch metadata.
