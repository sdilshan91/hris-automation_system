---
id: US-AUTH-004
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (all roles)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-004: Password reset flow

## 1. Description
**As a** tenant user who has forgotten my password,
**I want to** request a password reset link via email and set a new password,
**So that** I can regain access to my account without contacting an administrator.

## 2. Preconditions
- The user has a global `users` record with a non-null `password_hash` (i.e., they have used local credentials before) OR they are a social-only user who wants to add a local password.
- The tenant subdomain is valid and the tenant is in `active` or `trial` state.
- An SMTP relay or transactional email service is configured and operational.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user with an account in the platform | They submit their email to `POST /api/v1/auth/forgot-password` from a tenant subdomain | The system always returns HTTP 200 with a generic message ("If an account exists, a reset link has been sent") regardless of whether the email exists, to prevent user enumeration. |
| AC-2 | The email matches an active user with a membership in the resolved tenant | The forgot-password request is processed | The system generates a one-time reset token (ASP.NET Core Identity token provider), stores it securely, and sends an email with a reset link pointing to `https://{tenant}.yourhrm.com/reset-password?token={token}&email={email}`. |
| AC-3 | The user clicks the reset link and submits a new password | They call `POST /api/v1/auth/reset-password` with `{ email, token, newPassword }` | The system validates the token, updates the `password_hash`, sets `password_changed_at`, revokes all existing refresh tokens for this user across all tenants, resets `failed_login_count` to 0, clears `locked_until`, and returns 200 OK. |
| AC-4 | The user submits an expired or already-used reset token | They attempt to reset the password | The system returns 400 Bad Request with a message indicating the link has expired or already been used, and prompts the user to request a new one. |
| AC-5 | The user submits a new password that does not meet the tenant's password policy | They attempt to reset | The system returns 400 with specific validation errors (e.g., "Password must be at least 12 characters"). |
| AC-6 | A tenant admin triggers a forced password reset for a user via `POST /api/v1/tenant/users/{id}/force-password-reset` | The admin performs the action | The system revokes the user's refresh tokens, marks the account for password change, and sends the user a reset email. On next login attempt, the user is redirected to set a new password. |

## 4. Functional Requirements
- FR-1: The forgot-password endpoint SHALL be `POST /api/v1/auth/forgot-password` accepting `{ email: string }`.
- FR-2: The reset-password endpoint SHALL be `POST /api/v1/auth/reset-password` accepting `{ email, token, newPassword }`.
- FR-3: Reset tokens SHALL be generated using ASP.NET Core Identity's `UserManager.GeneratePasswordResetTokenAsync()`.
- FR-4: Reset tokens SHALL expire after a configurable duration (default: 1 hour).
- FR-5: The new password SHALL be validated against the tenant's password policy (minimum length, complexity, history).
- FR-6: Upon successful reset, ALL refresh tokens for the user (across all tenants) SHALL be revoked to force re-authentication everywhere.
- FR-7: The reset email SHALL be tenant-branded (logo, colors) and include the tenant name.
- FR-8: Password reset events SHALL be audited: `password_reset_requested` and `password_reset_completed`.
- FR-9: The forgot-password endpoint SHALL be rate-limited per IP and per email (e.g., max 5 requests per email per hour).

## 5. Non-Functional Requirements
- NFR-1: The forgot-password endpoint SHALL respond within 400 ms at P95 (email sending is asynchronous via background job).
- NFR-2: Reset tokens SHALL be cryptographically random and at least 256 bits of entropy.
- NFR-3: The reset email SHALL be dispatched within 60 seconds of the request.
- NFR-4: Password history SHALL store the last N password hashes (configurable, default 5) to prevent reuse.
- NFR-5: HTTPS is required for all reset-related endpoints and links.

## 6. Business Rules
- BR-1: The forgot-password response SHALL NOT reveal whether the email exists in the system (prevent enumeration).
- BR-2: Password policy is configurable per tenant: minimum length (default 12), complexity requirements (uppercase, lowercase, digit, special character), maximum age, and history count.
- BR-3: Social-only users who request a password reset SHALL be allowed to set a local password (effectively enabling dual authentication).
- BR-4: A successful password reset clears any account lockout (`locked_until` = null, `failed_login_count` = 0).
- BR-5: The reset link is scoped to the tenant subdomain it was requested from; it cannot be used on a different tenant's subdomain.

## 7. Data Requirements
- **Forgot-password input:** `{ email: string (max 150) }`
- **Reset-password input:** `{ email: string, token: string, newPassword: string }`
- **Output (both):** `{ message: string }` with appropriate HTTP status.
- **Tables affected:** `users` (password_hash, password_changed_at, failed_login_count, locked_until), `refresh_token` (bulk revocation).
- **Audit records:** `password_reset_requested` (email, IP, tenant_id), `password_reset_completed` (user_id, tenant_id, IP).
- **Email data:** tenant branding (logo_url, primary_color, tenant_name), reset link URL, expiry information.

## 8. UI/UX Notes
- Notion-like design: clean, centered card on the tenant-branded login page.
- "Forgot password?" link below the login form navigates to a single-field form (email input).
- After submission, display a success message regardless of email existence: "If an account with that email exists, we've sent a password reset link."
- The reset password page (from email link) shows: new password field, confirm password field, and password strength indicator.
- Password policy requirements displayed as a checklist that updates in real-time as the user types.
- Success state: "Password updated successfully. Redirecting to login..." with auto-redirect after 3 seconds.
- Mobile responsive: same centered card layout scaling to small screens.

## 9. Dependencies
- US-AUTH-001 (Login) for the authentication flow after password reset.
- US-AUTH-002 (JWT/Refresh) for token revocation on reset.
- SMTP/email service configuration.
- Tenant branding data (logo, colors) for email templates.
- Hangfire for asynchronous email dispatch.

## 10. Assumptions & Constraints
- Email delivery reliability depends on the configured SMTP/transactional email service.
- Reset tokens are single-use; once consumed, the same token cannot be reused.
- The tenant's password policy is applied at reset time (not the system default).
- Rate limiting on the forgot-password endpoint is essential to prevent email-bombing.

## 11. Test Hints
- **Happy path:** Request reset, click link, set new password, verify old tokens are revoked, verify login with new password works.
- **User enumeration:** Request reset for non-existent email; verify same 200 response and timing (no timing side-channel).
- **Expired token:** Wait for token expiry, attempt reset; verify 400.
- **Reused token:** Use token once successfully, try again; verify 400.
- **Password policy violation:** Submit weak password; verify validation errors returned.
- **Password history:** Reset to a recently used password; verify rejection.
- **Rate limiting:** Send 6+ requests for the same email; verify throttling.
- **Cross-tenant isolation:** Request reset on tenant A; verify link only works on tenant A's subdomain.
- **Admin forced reset:** Admin triggers forced reset; verify user must change password on next login.
- **Social-only user:** Social-only user requests reset; verify they can set a local password.
