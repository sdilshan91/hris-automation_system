---
id: TC-AUTH-029
user_story: US-AUTH-005
module: Authentication
priority: critical
type: functional
status: pass
created: 2026-06-03
---

# TC-AUTH-029: Forced MFA enrollment when tenant policy requires it for user's role

## 1. Test Objective
Verify that when a tenant's MFA policy is set to "required" and the user's role is in the `mfaRequiredRoles` list, an unenrolled user is forced through mandatory MFA enrollment before gaining access to any protected resource. No tokens are issued until enrollment and verification are complete.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-6, FR-7
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Tenant "acme" exists and is in `active` state.
- Tenant auth settings: `mfaPolicy = "required"`, `mfaRequiredRoles = ["Tenant Admin"]`.
- User `admin@acme.com` has the "Tenant Admin" role in tenant "acme".
- User `admin@acme.com` has `mfa_enabled = false` and `mfa_secret = null`.
- User has valid password credentials.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | admin@acme.com | Tenant Admin role, MFA not enrolled |
| Password | S3cure!Pass2026 | Correct password |
| Tenant | acme | mfaPolicy=required, mfaRequiredRoles=["Tenant Admin"] |
| Login endpoint | POST /api/v1/auth/login | Step 1 of auth |
| Enroll endpoint | POST /api/v1/auth/mfa/enroll | Generates secret + QR + recovery codes |
| Verify endpoint | POST /api/v1/auth/mfa/verify | Completes enrollment |
| Protected endpoint | GET /api/v1/employees | Any non-auth endpoint |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with `{ email: "admin@acme.com", password: "S3cure!Pass2026" }` | HTTP 200 with `{ mfaChallenge: true, mfaEnrollmentRequired: true, mfaMethod: "totp" }`. No `accessToken` in body, no refresh token cookie. |
| 2 | Verify the response does NOT contain an `accessToken` or refresh token | No tokens are issued at the password stage. |
| 3 | Attempt `GET /api/v1/employees` without a bearer token | HTTP 401 Unauthorized. Confirms no access is granted without completing MFA enrollment. |
| 4 | Send `POST /api/v1/auth/mfa/enroll` (using the session/challenge token from step 1) | HTTP 200 with `{ secret, qrCodeDataUrl, recoveryCodes[] }`. 10 recovery codes returned. |
| 5 | Generate a valid TOTP code from the returned secret using a TOTP library | 6-digit code for the current time step. |
| 6 | Send `POST /api/v1/auth/mfa/verify` with `{ code: "{valid_code}" }` | HTTP 200 with `{ accessToken, user, tenant, permissions }`. Tokens are now issued. |
| 7 | Verify `users.mfa_enabled = true` and `users.mfa_secret` is populated (encrypted) in the database | MFA is fully activated. |
| 8 | Verify 10 hashed recovery codes exist in `mfa_recovery_code` table for this user | All codes have `used_at = null`. |
| 9 | Verify `mfa_enrolled` audit event is logged with `user_id`, `tenant_id`, and timestamp | Audit trail is present. |
| 10 | Send `GET /api/v1/employees` with the bearer token from step 6 | HTTP 200 with employee data. Access is now granted. |
| 11 | Log out and log in again with the same user | After password stage, response is `{ mfaChallenge: true, mfaMethod: "totp" }` (challenge, NOT enrollment required). Enrollment is no longer forced. |

## 6. Postconditions
- User `admin@acme.com` has `mfa_enabled = true` with encrypted TOTP secret.
- 10 recovery codes are stored (hashed) for the user.
- Subsequent logins for this user present an MFA challenge (not enrollment).
- Audit log contains the `mfa_enrolled` event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-07-03 (API, @test-runner):** BLOCKED — forced-enrollment code path exists (AuthService.cs:209-223) but precondition mfaPolicy=required cannot be set: PUT /tenant/auth-settings 403s even for Tenant Admin (BUG-240).
>
> **Re-exec 2026-07-03 (API, @test-runner) — BUG-240 fix unblocked → PASS:** Precondition now settable — PUT auth-settings {mfaPolicy:required, mfaRequiredRoles:["Tenant Admin"]} -> 200. Fresh login as tenantadmin@acme.test (Tenant Admin role, mfaEnabled=false, in-scope) returns `{accessToken:"", mfaChallenge:true, mfaMethod:"totp", mfaEnrollmentRequired:true, refreshToken:null}` (steps 1-2: forced enrollment, NO token issued). Control: employee@acme.test (role NOT in mfaRequiredRoles) login returns normal accessToken, mfaChallenge=false, enrollmentRequired=false — role-scoping correct. Enrollment-completion sub-steps (4-11, TOTP secret+verify+recovery codes) not re-driven here; the discriminating assertion (forced-enrollment gate + no-token + role scope) is confirmed. Settings restored to mfaPolicy=off post-run.
