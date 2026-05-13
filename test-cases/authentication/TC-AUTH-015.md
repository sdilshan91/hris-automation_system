---
id: TC-AUTH-015
user_story: US-AUTH-005
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-015: Login with invalid TOTP code fails

## 1. Test Objective
Verify that submitting an incorrect TOTP code during the MFA challenge returns a 401 error, increments the failed attempt counter, and triggers account lockout after 5 consecutive failures.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2, FR-3
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- User `john@acme.com` has MFA enabled and has passed the password stage of login (MFA challenge is active).
- The user's failed attempt counter is at 0.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | MFA-enabled user |
| Invalid TOTP Code | 000000 | Incorrect code |
| Another Invalid Code | 999999 | Incorrect code |
| Lockout threshold | 5 | Consecutive failed MFA attempts |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Complete the password stage of login to reach the MFA challenge | MFA challenge response received (`mfaChallenge: true`). |
| 2 | Send `POST /api/v1/auth/mfa/verify` with `{ code: "000000" }` (invalid) | HTTP 401 with "Invalid verification code." |
| 3 | Verify no tokens are issued | No `accessToken` in body, no refresh token cookie. |
| 4 | Verify the failed attempt counter is incremented | Counter is now 1. |
| 5 | Verify an `mfa_challenge_failure` audit event is logged | Audit record with user_id, tenant_id. |
| 6 | Submit 4 more incorrect TOTP codes (total of 5 failures) | Each returns HTTP 401 with "Invalid verification code." |
| 7 | On the 5th failure, verify the account is temporarily locked | Account `locked_until` is set; response indicates temporary lockout. |
| 8 | Attempt to submit a valid TOTP code while the account is locked | HTTP 401 with lockout message; valid code does not bypass lockout. |
| 9 | Verify the rate limit is enforced on MFA verification attempts | After 5 attempts per session, further attempts are throttled. |
| 10 | Verify MFA failures count toward the same lockout threshold as password failures (shared counter per US-AUTH-010 FR-10) | Failed MFA attempts increment the global `failed_login_count`. |

## 6. Postconditions
- No tokens have been issued.
- After 5 failures, the account is temporarily locked.
- Failed attempt counter reflects all MFA failures.
- Audit events recorded for each failure and for the lockout.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
