---
id: US-AUTH-005
module: Authentication & Authorization
priority: Should Have
persona: Tenant Admin / Tenant User
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-005: Multi-factor authentication (TOTP) -- optional per tenant

## 1. Description
**As a** tenant admin,
**I want to** configure MFA (TOTP) policy for my organization -- off, optional, or required (with per-role overrides),
**So that** I can enforce stronger authentication for privileged roles while keeping it flexible for other users.

**As a** tenant user,
**I want to** enroll in and use TOTP-based multi-factor authentication,
**So that** my account is protected even if my password is compromised.

## 2. Preconditions
- The user is authenticated and has an active membership in the tenant.
- The tenant is in `active` or `trial` state.
- The MFA feature is available on the tenant's subscription plan.
- For enrollment: the user has access to a TOTP-compatible authenticator app (Google Authenticator, Authy, Microsoft Authenticator, etc.).

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant admin configures MFA policy to "required" for admin roles | An admin user without MFA enrolled attempts to log in | The system authenticates the password, but instead of issuing full tokens, returns a mandatory MFA enrollment response; the user is redirected to the MFA setup page before accessing any other feature. |
| AC-2 | A user initiates MFA enrollment via `POST /api/v1/auth/mfa/enroll` | The system processes the enrollment request | The system generates a TOTP secret, stores it encrypted in `users.mfa_secret`, and returns a `otpauth://` URI and a QR code data URL for the user to scan with their authenticator app, along with recovery codes. |
| AC-3 | A user scans the QR code and submits a valid TOTP code via `POST /api/v1/auth/mfa/verify` | The system verifies the code | The system validates the code against the stored secret, sets `users.mfa_enabled = true`, and confirms enrollment. Recovery codes are shown once and must be saved by the user. |
| AC-4 | A user with MFA enabled logs in with correct email and password | They submit credentials without an MFA code | The system returns HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }` and no access/refresh tokens, prompting the frontend to display the TOTP input. |
| AC-5 | A user submits an incorrect TOTP code during login | They attempt to complete MFA | The system returns 401 with "Invalid verification code" and increments the failed attempt counter. After 5 consecutive failed MFA attempts, the account is temporarily locked. |
| AC-6 | A tenant admin sets MFA policy to "optional" | Users in that tenant choose whether to enable MFA | Users can enroll or skip MFA from their profile settings; the system does not force enrollment. |
| AC-7 | A user loses access to their authenticator app | They use a recovery code during the MFA challenge | The system accepts the valid recovery code, allows login, and marks the recovery code as used. The user is prompted to re-enroll MFA or generate new recovery codes. |

## 4. Functional Requirements
- FR-1: The MFA enrollment endpoint SHALL be `POST /api/v1/auth/mfa/enroll`, returning `{ secret, qrCodeDataUrl, recoveryCodes[] }`.
- FR-2: The MFA verification endpoint SHALL be `POST /api/v1/auth/mfa/verify`, accepting `{ code: string }`.
- FR-3: TOTP SHALL use the `Otp.NET` library with SHA1 algorithm, 6-digit codes, and 30-second time steps (RFC 6238 compliant).
- FR-4: The TOTP secret SHALL be stored encrypted at the column level in `users.mfa_secret` using `pgcrypto` envelope encryption.
- FR-5: The system SHALL generate 10 single-use recovery codes during enrollment, stored as hashed values.
- FR-6: Tenant MFA policy SHALL be configurable via `PUT /api/v1/tenant/auth-settings` with options: `{ mfaPolicy: "off" | "optional" | "required", mfaRequiredRoles: string[] }`.
- FR-7: When MFA is required for a role and the user has not enrolled, login SHALL succeed at the password stage but redirect to mandatory enrollment before granting access to any other endpoint.
- FR-8: MFA enrollment and disable events SHALL be written to the tenant audit log.
- FR-9: A user SHALL be able to disable MFA from their profile if the tenant policy is "optional" and their roles do not require it.
- FR-10: TOTP validation SHALL accept codes within a +/- 1 time-step window to accommodate clock drift.

## 5. Non-Functional Requirements
- NFR-1: MFA verification response time SHALL be <= 200 ms at P95.
- NFR-2: TOTP secrets SHALL be encrypted at rest with a key encryption key (KEK) stored in the secrets vault.
- NFR-3: Recovery codes SHALL be displayed only once during enrollment; they are never retrievable again (only regeneratable).
- NFR-4: The MFA challenge step SHALL be rate-limited to prevent brute-force code guessing (max 5 attempts per session).
- NFR-5: QR code generation SHALL happen server-side and be returned as a data URL to avoid external service dependencies.

## 6. Business Rules
- BR-1: MFA policy is per-tenant with per-role overrides. A tenant can require MFA for "Tenant Admin" and "HR Officer" roles while leaving it optional for "Employee."
- BR-2: A user who belongs to multiple tenants manages MFA at the global user level (one TOTP secret), but the enforcement policy is per-tenant. If any tenant they belong to requires MFA, they must have it enabled globally.
- BR-3: Disabling MFA when the tenant policy requires it for the user's role is not allowed.
- BR-4: Recovery codes are single-use; each used code is marked and cannot be reused.
- BR-5: When a tenant admin changes MFA policy from "optional" to "required," existing users in affected roles who have not enrolled are prompted on their next login.

## 7. Data Requirements
- **`users` table:** `mfa_enabled` (boolean), `mfa_secret` (varchar 200, encrypted).
- **New table `mfa_recovery_code`:** `recovery_code_id` (uuid PK), `user_id` (FK), `code_hash` (varchar 500), `used_at` (timestamptz, nullable), `created_at` (timestamptz).
- **Tenant auth settings (in `tenant_setting` or similar):** `mfa_policy` (enum), `mfa_required_roles` (jsonb array of role names).
- **Enrollment response:** `{ secret: string, qrCodeDataUrl: string, recoveryCodes: string[] }`.
- **Audit records:** `mfa_enrolled`, `mfa_disabled`, `mfa_challenge_success`, `mfa_challenge_failure`, `mfa_recovery_code_used`.

## 8. UI/UX Notes
- Notion-like clean design for the MFA enrollment flow:
  1. Step 1: Display QR code with a scannable code card and a "copy secret" fallback link.
  2. Step 2: 6-digit code input with auto-focus and auto-submit on 6th digit.
  3. Step 3: Display recovery codes in a copyable/downloadable list with a warning to save them.
- MFA challenge on login: a separate card/step with a 6-digit input, "Use recovery code" link, and a subtle "Back to login" link.
- In user profile settings: toggle to enable/disable MFA (if allowed by policy), "Regenerate recovery codes" button.
- When MFA is required by policy and not yet enrolled, display a full-page enrollment flow that cannot be bypassed.
- Mobile: same flow with appropriately sized QR code and input fields.

## 9. Dependencies
- US-AUTH-001 (Login) for the two-step authentication flow.
- US-AUTH-002 (JWT/Refresh) for token issuance after MFA verification.
- US-AUTH-006 (RBAC) for per-role MFA enforcement.
- `Otp.NET` NuGet package for TOTP generation/validation.
- Secrets vault for TOTP secret encryption key.

## 10. Assumptions & Constraints
- Only TOTP is supported in Phase 1; SMS-based MFA and hardware keys (FIDO2/WebAuthn) are deferred.
- The authenticator app is the user's responsibility; the platform does not distribute one.
- MFA enrollment is global to the user, not per-tenant; but enforcement is per-tenant policy.
- Recovery code generation uses a cryptographically secure random generator.

## 11. Test Hints
- **Happy path enrollment:** Enroll, scan QR, verify code, confirm `mfa_enabled = true`.
- **Login with MFA:** Verify two-step flow: password -> MFA challenge -> tokens issued.
- **Invalid TOTP code:** Submit wrong code; verify rejection and counter increment.
- **Lockout after MFA failures:** 5 wrong codes; verify temporary lock.
- **Recovery code:** Use recovery code; verify login succeeds and code is marked used.
- **Recovery code reuse:** Use same code again; verify rejection.
- **Policy enforcement:** Set policy to "required" for admin roles; verify unenrolled admin is forced to enroll.
- **Optional policy:** Verify users can freely enable/disable MFA.
- **Cross-tenant:** User with MFA enabled accesses tenant with "off" policy; verify MFA is still checked (global enforcement).
- **Time drift:** Verify codes from +/- 1 time step are accepted.
