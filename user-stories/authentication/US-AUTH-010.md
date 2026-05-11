---
id: US-AUTH-010
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (all roles) / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-010: Account lockout after failed attempts

## 1. Description
**As a** platform security measure,
**I want** user accounts to be automatically locked after a configurable number of consecutive failed login attempts,
**So that** brute-force and credential-stuffing attacks are mitigated, protecting user accounts and tenant data.

**As a** tenant admin,
**I want to** configure the lockout policy and manually unlock locked accounts,
**So that** I can balance security with usability for my organization.

## 2. Preconditions
- The user has a global `users` record with `failed_login_count` and `locked_until` fields.
- The tenant's lockout policy is configured (or defaults apply).
- The login flow (US-AUTH-001) is implemented.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user has failed login attempts below the lockout threshold (default: 5) | They submit an incorrect password | The system increments `failed_login_count`, returns 401 with a generic error ("Invalid email or password"), and does not reveal the remaining attempts count. |
| AC-2 | A user reaches the maximum failed attempts (default: 5 consecutive failures) | They submit another incorrect password | The system sets `locked_until` to current time + lockout duration (default: 15 minutes), returns 401 with "Account temporarily locked. Try again later or contact your administrator," and logs an `account_locked` audit event. |
| AC-3 | A locked user attempts to log in with correct credentials before the lockout expires | They submit their valid password | The system returns 401 with the lockout message; correct credentials do NOT bypass the lockout. |
| AC-4 | The lockout duration expires | The user attempts to log in with correct credentials | The system clears `locked_until` and `failed_login_count`, processes the login normally, and issues tokens. |
| AC-5 | A tenant admin manually unlocks a locked user account | They call the unlock endpoint or use the admin UI | The system sets `locked_until = null` and `failed_login_count = 0`, logs an `account_unlocked_by_admin` audit event, and the user can immediately attempt to log in. |
| AC-6 | A user successfully logs in after one or more failed attempts (below the lockout threshold) | They submit the correct password | The system resets `failed_login_count` to 0, clearing the failed attempt history, and issues tokens normally. |

## 4. Functional Requirements
- FR-1: The system SHALL track consecutive failed login attempts in `users.failed_login_count`.
- FR-2: On reaching the maximum failed attempts, the system SHALL set `users.locked_until` to `DateTime.UtcNow + lockoutDuration`.
- FR-3: The lockout policy SHALL be configurable per tenant via the password policy settings: `maxFailedAttempts` (default: 5), `lockoutDurationMinutes` (default: 15).
- FR-4: On successful login, the system SHALL reset `failed_login_count` to 0 and `locked_until` to null.
- FR-5: The login flow SHALL check `locked_until` before verifying credentials; if the account is locked and the lockout has not expired, return 401 immediately without checking the password.
- FR-6: Tenant admins SHALL be able to unlock accounts via the user management interface.
- FR-7: Account lockout and unlock events SHALL be written to both the tenant audit log and system audit log.
- FR-8: The system SHALL send a notification to the user's email when their account is locked, including instructions to wait or contact their admin.
- FR-9: Progressive lockout SHALL be supported: after repeated lockouts (e.g., 3 lockout cycles within 24 hours), the lockout duration doubles.
- FR-10: Failed MFA attempts SHALL also count toward the lockout threshold (shared counter with password failures).

## 5. Non-Functional Requirements
- NFR-1: The lockout check SHALL add <= 2 ms overhead to the login flow (single indexed column lookup).
- NFR-2: The `failed_login_count` update SHALL be atomic (use database-level increment) to handle concurrent login attempts.
- NFR-3: Account lockout notifications SHALL be sent within 60 seconds of the lockout event (via Hangfire background job).
- NFR-4: The lockout mechanism SHALL be resistant to timing attacks; response time for locked vs. unlocked accounts should be indistinguishable.
- NFR-5: Lockout state SHALL be stored in the database (not in-memory or Redis only) to ensure persistence across API instance restarts.

## 6. Business Rules
- BR-1: Lockout is per global user account, not per tenant membership. If a user is locked out, they cannot log in to ANY tenant.
- BR-2: Password reset (US-AUTH-004) clears the lockout: `failed_login_count = 0`, `locked_until = null`.
- BR-3: A tenant admin can only unlock users who have a membership in their tenant; they cannot unlock users in other tenants.
- BR-4: System admins can unlock any user account regardless of tenant.
- BR-5: The lockout policy values are bounded: `maxFailedAttempts` between 3 and 10, `lockoutDurationMinutes` between 5 and 60.
- BR-6: Social login failures (e.g., IdP returns an error) do NOT increment the lockout counter; only local credential and MFA failures do.
- BR-7: Account lockout does not affect already-active sessions (existing refresh tokens remain valid); it only prevents new logins.

## 7. Data Requirements
- **`users` table fields:** `failed_login_count` (int, default 0), `locked_until` (timestamptz, nullable).
- **Tenant password policy settings:** `max_failed_attempts` (int, default 5), `lockout_duration_minutes` (int, default 15), `progressive_lockout_enabled` (boolean, default false).
- **Audit records:** `login_failure` (includes attempt count), `account_locked` (user_id, IP, attempt count, lockout_until), `account_unlocked_by_admin` (user_id, admin_user_id), `account_unlocked_by_timeout` (user_id).
- **Email notification data:** user email, tenant name(s), lockout timestamp, lockout duration, support contact link.

## 8. UI/UX Notes
- Notion-like design for lockout messaging:
  - On lockout: the login form displays a subtle but clear error banner: "Your account has been temporarily locked due to too many failed login attempts. Please try again in {X} minutes or contact your administrator."
  - Do NOT display a countdown timer (to avoid timing information leakage).
  - After lockout expires, the error banner disappears on the next attempt.
- Tenant Admin > User Management: locked accounts show a "Locked" badge with a red indicator and an "Unlock" action button.
- Tenant Admin > Security Settings: lockout policy configuration with clean form fields for max attempts and lockout duration.
- User's own profile: a "Security" section showing recent login activity (last 5 successful/failed logins with timestamps and locations).
- Mobile: same lockout messages displayed inline within the mobile login form.

## 9. Dependencies
- US-AUTH-001 (Login) for the authentication flow where lockout is checked.
- US-AUTH-004 (Password reset) for lockout clearing on password reset.
- US-AUTH-005 (MFA) for shared failed attempt counting.
- Tenant audit logging infrastructure.
- Email notification service (Hangfire + SMTP).

## 10. Assumptions & Constraints
- Lockout is applied at the global user level, which means a brute-force attack on one tenant's login page locks the user out of all tenants. This is a deliberate security trade-off.
- The lockout mechanism complements (does not replace) rate limiting on the login endpoint; both work together.
- ASP.NET Core Identity's built-in lockout mechanism (`UserManager.AccessFailedAsync`, `IsLockedOutAsync`) can be leveraged with customization for multi-tenant policy values.
- Progressive lockout (doubling duration) is optional and configurable per tenant.

## 11. Test Hints
- **Happy path lockout:** Fail 5 times, verify account is locked, verify correct credentials are rejected during lockout, wait for expiry, verify login succeeds.
- **Reset on success:** Fail 3 times, then succeed; verify `failed_login_count` resets to 0.
- **Admin unlock:** Lock an account, admin unlocks it, verify immediate login is possible.
- **Password reset clears lockout:** Lock an account, reset password, verify lockout is cleared.
- **Progressive lockout:** Trigger 3 lockout cycles; verify duration doubles each time.
- **MFA failures count:** Fail MFA verification 5 times; verify account lockout triggers.
- **Cross-tenant impact:** Lock user via tenant A login page; verify login is blocked on tenant B too (global lockout).
- **Timing attack resistance:** Compare response times for locked vs. non-existent accounts; verify they are similar.
- **Concurrent attempts:** Fire multiple login requests simultaneously with wrong password; verify atomic increment (no race condition in counter).
- **Audit trail:** Verify lockout and unlock events are recorded with all required metadata.
- **Notification:** Verify lockout notification email is sent within 60 seconds.
