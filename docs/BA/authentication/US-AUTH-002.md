---
id: US-AUTH-002
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (all roles)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-002: JWT token issuance and refresh token flow

## 1. Description
**As a** tenant user,
**I want to** receive a short-lived JWT access token and a long-lived refresh token upon login, with the ability to silently refresh my session,
**So that** I can maintain a seamless, secure session without repeatedly entering my credentials.

## 2. Preconditions
- The user has successfully authenticated (via US-AUTH-001 or another valid authentication mechanism).
- The tenant is in an `active` or `trial` state.
- The user has an active `user_tenant` membership for the resolved tenant.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user successfully authenticates | The system issues tokens | A JWT access token is returned in the response body (RS256 signed, ~15 min expiry) and a refresh token is set as an `httpOnly; Secure; SameSite=Strict` cookie (~7 day expiry). |
| AC-2 | A user's access token is about to expire or has expired | The Angular HTTP interceptor sends `POST /api/v1/auth/refresh` with the httpOnly cookie | The system validates the refresh token hash against the `refresh_token` table, issues a new access token and a new rotated refresh token, revokes the old refresh token, and returns the new access token in the response body. |
| AC-3 | A refresh token has already been used (rotated) and is presented again | The system receives the reused token | The system detects reuse, revokes the entire token chain for that user-tenant session, and returns 401 Unauthorized, forcing re-authentication. The user is notified of a potential security event. |
| AC-4 | A refresh token has expired (past `expires_at`) | The user attempts to refresh | The system returns 401 Unauthorized; the frontend redirects to the login page. |
| AC-5 | A refresh token is presented for a tenant that is now `suspended` or `terminated` | The user attempts to refresh | The system returns 403 Forbidden and does not issue new tokens. |
| AC-6 | A user's membership status changes to `disabled` while they have a valid refresh token | The user attempts to refresh | The system checks current membership status, returns 403, and revokes remaining tokens for that membership. |
| AC-7 | The JWT signing key is rotated | Existing tokens signed with the previous key are presented | The system validates tokens signed with the previous key for the remainder of their lifetime (key overlap period), while new tokens are signed with the current key. |

## 4. Functional Requirements
- FR-1: The JWT access token SHALL contain the claims: `sub` (user_id), `email`, `tenant_id`, `user_tenant_id`, `roles[]`, `permissions[]`, `is_impersonation` (boolean), `iat`, `exp`.
- FR-2: Access tokens SHALL be signed with RS256 using a key from the secrets vault.
- FR-3: Access token lifetime SHALL be approximately 15 minutes with a clock skew tolerance of 30 seconds.
- FR-4: The refresh token SHALL be a cryptographically random opaque string, stored as a SHA-256 hash in the `refresh_token` table.
- FR-5: Refresh token lifetime SHALL be approximately 7 days.
- FR-6: On each refresh, the system SHALL rotate the token: revoke the old token (set `revoked_at`), issue a new token, and link them via `replaced_by_token_id`.
- FR-7: Reuse detection SHALL revoke the entire chain: all tokens sharing the same origin session for that user + tenant.
- FR-8: The refresh endpoint SHALL be `POST /api/v1/auth/refresh`.
- FR-9: The system SHALL record `user_agent` and `ip_address` on each refresh token for audit purposes.
- FR-10: The Angular HTTP interceptor SHALL queue concurrent 401 responses and replay them after a single silent refresh completes.

## 5. Non-Functional Requirements
- NFR-1: Token refresh API response time SHALL be <= 200 ms at P95.
- NFR-2: Refresh tokens SHALL be stored as SHA-256 hashes; raw tokens are never persisted.
- NFR-3: JWT signing key rotation SHALL occur quarterly; previous key remains valid for one full token lifetime after rotation.
- NFR-4: The refresh endpoint SHALL be rate-limited to prevent token-grinding attacks.
- NFR-5: All token operations SHALL occur over HTTPS (TLS 1.2+).

## 6. Business Rules
- BR-1: A refresh token is bound to a specific `user_id` + `tenant_id` pair; it cannot be used to obtain tokens for a different tenant.
- BR-2: Impersonation JWTs SHALL NOT be refreshable past their original expiry; impersonation sessions require explicit re-initiation.
- BR-3: If tenant status transitions to `past_due`, refresh continues but the access token includes a claim or flag indicating read-only mode after the grace period.
- BR-4: Expired refresh tokens SHALL be cleaned up by a background job (Hangfire) periodically.

## 7. Data Requirements
- **`refresh_token` table:** `refresh_token_id` (uuid PK), `user_id` (FK), `tenant_id` (FK), `token_hash` (varchar 500), `issued_at`, `expires_at`, `revoked_at` (nullable), `replaced_by_token_id` (nullable), `user_agent` (varchar 500), `ip_address` (varchar 50).
- **JWT claims payload:** `{ sub, email, tenant_id, user_tenant_id, roles[], permissions[], is_impersonation, iat, exp, iss, aud }`.
- **Refresh request:** Cookie-based (no request body needed); the httpOnly cookie is automatically attached by the browser.
- **Refresh response:** `{ accessToken: string }` (new refresh token is set via Set-Cookie header).

## 8. UI/UX Notes
- Notion-like seamless experience: the user never sees a "refreshing session" indicator.
- The Angular HTTP interceptor handles 401 silently; if refresh fails, the user is redirected to the login page with a "Session expired" toast notification.
- No visible token management UI for end users.
- On reuse detection (forced logout), display a security alert: "Your session was terminated for security reasons. Please log in again."

## 9. Dependencies
- US-AUTH-001 (Login) provides the initial token issuance.
- Secrets vault integration for JWT signing key management.
- Redis cache for optional token blacklist lookups (performance optimization).
- Hangfire for expired token cleanup background job.

## 10. Assumptions & Constraints
- The SPA stores the access token in memory only (not localStorage) to minimize XSS exposure.
- The refresh token cookie is scoped to the API domain and not accessible to JavaScript.
- The platform uses a single signing key pair at a time (with overlap during rotation), not per-tenant keys.
- Clock synchronization between API servers is maintained within acceptable NTP drift.

## 11. Test Hints
- **Happy path refresh:** Authenticate, wait for token expiry, verify silent refresh works and new tokens are issued.
- **Token rotation:** After refresh, verify old refresh token is marked `revoked_at` and new token has `replaced_by_token_id` set.
- **Reuse detection:** Use an old refresh token after rotation; verify entire chain is revoked and 401 is returned.
- **Expired refresh token:** Present token past `expires_at`; verify 401.
- **Suspended tenant:** Refresh with valid token but suspended tenant; verify 403.
- **Disabled membership:** Disable user_tenant mid-session; verify refresh returns 403.
- **Key rotation:** Issue token with old key, rotate key, verify token still validates during overlap.
- **Concurrent refresh:** Fire two refresh requests simultaneously; verify only one succeeds and the other triggers reuse detection gracefully (or queuing on the frontend).
- **Cross-tenant isolation:** Verify a refresh token issued for tenant A cannot be used to obtain tokens for tenant B.
