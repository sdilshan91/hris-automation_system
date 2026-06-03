---
id: TC-AUTH-031
user_story: US-AUTH-005
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-031: Recovery code reuse rejection

## 1. Test Objective
Verify that a recovery code that has already been used cannot be reused for a subsequent MFA challenge. The system must reject the code with a 401 response, increment the failed attempt counter, and log the attempt.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-7 (negative path)
- Functional Requirements: FR-5
- Business Rules: BR-4
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- User `john@acme.com` has MFA enabled with a valid TOTP secret.
- The user previously used recovery code "ABCD-1234-EFGH" to complete an MFA challenge (as per TC-AUTH-030). That code's `used_at` field is non-null in the database.
- The user has initiated a new login and passed the password stage, reaching the MFA challenge.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | MFA-enabled user |
| Password | S3cure!Pass2026 | Correct password |
| Used Recovery Code | ABCD-1234-EFGH | Already used in a prior login |
| Unused Recovery Code | WXYZ-5678-IJKL | Valid, unused code |
| Challenge endpoint | POST /api/v1/auth/mfa/challenge | Accepts recovery code |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with valid credentials | HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }`. |
| 2 | Send `POST /api/v1/auth/mfa/challenge` with `{ recoveryCode: "ABCD-1234-EFGH" }` (the already-used code) | HTTP 401 with `{ error: "Invalid verification code" }`. |
| 3 | Verify no tokens are issued | No `accessToken` in body, no refresh token cookie set. |
| 4 | Verify the failed MFA attempt counter is incremented | Counter reflects the failed recovery code attempt. |
| 5 | Verify an `mfa_challenge_failure` audit event is logged | Audit record indicates recovery code reuse attempt with `user_id`, `tenant_id`, timestamp. |
| 6 | Verify the `used_at` field for the used code is unchanged | No state corruption on the recovery code record. |
| 7 | Send `POST /api/v1/auth/mfa/challenge` with `{ recoveryCode: "WXYZ-5678-IJKL" }` (a valid, unused code) | HTTP 200 with `{ accessToken, user, tenant, permissions }`. Tokens are issued. |
| 8 | Verify the second recovery code is now marked as used | `used_at` is set for "WXYZ-5678-IJKL". |
| 9 | Repeat reuse test: initiate a new login and attempt to use "WXYZ-5678-IJKL" again | HTTP 401 with `{ error: "Invalid verification code" }`. Confirms the single-use invariant holds for all codes. |

## 6. Postconditions
- The already-used recovery code remains rejected on all subsequent attempts.
- Failed attempt counter reflects the rejected attempts.
- Audit log records each failed recovery code reuse attempt.
- A valid unused code still works, confirming granular per-code tracking.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
