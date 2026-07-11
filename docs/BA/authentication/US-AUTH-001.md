---
id: US-AUTH-001
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (Employee / Manager / HR Officer / Tenant Admin)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-001: Admin login with username and password

## 1. Description
**As a** tenant user (Employee, Manager, HR Officer, or Tenant Admin),
**I want to** log in to my tenant workspace using my email and password,
**So that** I can securely access the HRM platform features assigned to my role within my organization.

## 2. Preconditions
- The tenant exists with status `trial` or `active` in the platform.
- The tenant subdomain (e.g., `acme.yourhrm.com`) is provisioned and resolvable.
- The user has a global `users` record with a non-null `password_hash`.
- The user has an active `user_tenant` membership (status = `active`) for the resolved tenant.
- The user account is not globally locked (`locked_until` is null or in the past).

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user with valid credentials and an active membership in the resolved tenant | They submit their email and correct password on the tenant login page | The system returns a JWT access token (with `tenant_id`, `roles[]`, `permissions[]` claims) and sets an httpOnly refresh token cookie; the user is redirected to the tenant dashboard. |
| AC-2 | A user submits an incorrect password | They attempt to log in | The system returns a 401 Unauthorized with a generic message ("Invalid email or password"), increments `failed_login_count`, and does not reveal whether the email exists. |
| AC-3 | A user with valid credentials but no active membership in the resolved tenant | They attempt to log in on that tenant subdomain | The system returns a 403 Forbidden indicating no active membership for this organization. |
| AC-4 | A user attempts to log in on a tenant with status `suspended` or `terminated` | They submit valid credentials | The system returns a 403 with a tenant-specific message explaining the workspace is unavailable and does not issue tokens. |
| AC-5 | A user with MFA enabled on their account | They submit valid email and password without an MFA code | The system returns HTTP 200 with a `mfaChallenge` payload and no tokens, prompting the frontend to display the TOTP input. |
| AC-6 | A user navigates to an unprovisioned subdomain (e.g., `unknown.yourhrm.com`) | The login page attempts to load | The system returns a 404 Not Found; no login form or SPA shell is rendered. |

## 4. Functional Requirements
- FR-1: The login endpoint SHALL be `POST /api/v1/auth/login` accepting `{ email, password, mfaCode? }`.
- FR-2: The system SHALL resolve the tenant from the request subdomain via Tenant Resolution Middleware before processing credentials.
- FR-3: Password verification SHALL use BCrypt or ASP.NET Core Identity's default hasher.
- FR-4: On successful authentication, the system SHALL issue a JWT access token (~15 min expiry, RS256 signed) containing claims: `sub`, `email`, `tenant_id`, `user_tenant_id`, `roles[]`, `permissions[]`, `iat`, `exp`.
- FR-5: The refresh token SHALL be set as an `httpOnly; Secure; SameSite=Strict` cookie with ~7-day expiry.
- FR-6: The response body SHALL include `{ accessToken, user, tenant, permissions }`.
- FR-7: The system SHALL check the `user_tenant.status` is `active` for the resolved tenant before issuing tokens.
- FR-8: The system SHALL check the tenant lifecycle state and reject login for `suspended`, `terminating`, or `terminated` tenants (except tenant admin read-only access for `terminating`).
- FR-9: Login success and failure events SHALL be written to the tenant audit log.

## 5. Non-Functional Requirements
- NFR-1: Login API response time SHALL be <= 400 ms at P95.
- NFR-2: Password hashing SHALL use a cost factor that takes >= 250 ms on target hardware to resist brute-force attacks.
- NFR-3: All login traffic SHALL occur over HTTPS (TLS 1.2+).
- NFR-4: The login endpoint SHALL be rate-limited per IP and per email to mitigate credential-stuffing attacks.
- NFR-5: Error messages SHALL NOT reveal whether an email address is registered (prevent user enumeration).

## 6. Business Rules
- BR-1: Email comparison is case-insensitive (stored and matched in lowercase).
- BR-2: Social-only users (null `password_hash`) cannot use this flow; they must use social login or set a password via the password reset flow.
- BR-3: If the tenant policy requires MFA and the user has not enrolled, login SHALL succeed but the user SHALL be redirected to mandatory MFA enrollment before accessing any other page.
- BR-4: A user's global `is_active` flag must be `true` in addition to per-tenant membership status.

## 7. Data Requirements
- **Input:** `{ email: string (max 150), password: string, mfaCode?: string (6 digits) }`
- **Output:** `{ accessToken: string, user: { userId, email, displayName }, tenant: { tenantId, subdomain, name }, permissions: string[] }`
- **Tables involved:** `users`, `user_tenant`, `user_tenant_role`, `role_permission`, `tenant`, `refresh_token`
- **Audit record:** auth event type `login_success` or `login_failure` with IP address, user agent, and timestamp.

## 8. UI/UX Notes
- Notion-like clean login page with tenant branding (logo + primary color) loaded from the tenant profile.
- Single centered card with email and password fields, a "Log in" button, and a "Forgot password?" link.
- Social login buttons are hidden in local dev phase (deferred).
- On MFA challenge, the card smoothly transitions to a TOTP code input with a 6-digit field.
- Loading spinner on the button during authentication; disabled to prevent double-submit.
- Error messages appear as subtle inline alerts below the form, not browser alerts.
- Mobile responsive: form fills available width on screens < 480px with appropriate padding.

## 9. Dependencies
- US-AUTH-007 (Tenant resolution from subdomain) must be implemented for tenant context.
- Tenant provisioning (System Admin module) must have created the tenant and user membership.
- ASP.NET Core Identity configured with PostgreSQL stores.

## 10. Assumptions & Constraints
- Social logins (Google, Microsoft, Apple) are deferred to a later phase; only local credentials are implemented now.
- The platform uses a shared-database, shared-schema multi-tenancy model with `tenant_id` discriminator.
- JWT signing key is stored in a secrets vault, not in application configuration files.
- The Angular 20 SPA extracts the tenant subdomain on bootstrap and includes it contextually in all API calls.

## 11. Test Hints
- **Happy path:** Valid credentials + active membership -> JWT issued with correct claims.
- **Wrong password:** Verify 401 response and `failed_login_count` increment.
- **No membership:** User exists globally but not in this tenant -> 403.
- **Suspended tenant:** Login blocked with appropriate message.
- **MFA required:** Returns challenge payload, no tokens.
- **Unprovisioned subdomain:** Verify 404, no SPA shell rendered.
- **Case insensitivity:** Login with mixed-case email variant succeeds.
- **Concurrent requests:** Rapid login attempts from same IP are rate-limited.
- **Cross-tenant isolation:** Verify JWT `tenant_id` claim matches the resolved subdomain tenant, not any other tenant the user belongs to.
