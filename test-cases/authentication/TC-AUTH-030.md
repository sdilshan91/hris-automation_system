---
id: TC-AUTH-030
user_story: US-AUTH-005
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-030: Login with valid recovery code

## 1. Test Objective
Verify that a user who has lost access to their authenticator app can complete the MFA challenge by submitting a valid, unused recovery code. The system issues tokens, marks the recovery code as used, logs the event, and prompts the user to regenerate codes.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-7
- Functional Requirements: FR-2, FR-5
- Business Rules: BR-4

## 3. Preconditions
- User `john@acme.com` has MFA enabled (`mfa_enabled = true`) with a valid TOTP secret.
- The user has 10 unused recovery codes stored (hashed) in the `mfa_recovery_code` table.
- The user knows one of the plaintext recovery codes (saved during enrollment).
- The user has passed the password stage and is at the MFA challenge step.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | MFA-enabled user |
| Password | S3cure!Pass2026 | Correct password |
| Recovery Code | ABCD-1234-EFGH | One of the 10 codes from enrollment (example format) |
| Challenge endpoint | POST /api/v1/auth/mfa/challenge | Accepts recovery code |
| Audit event | mfa_recovery_code_used | Expected audit type |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with valid credentials | HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }`. No tokens issued. |
| 2 | Send `POST /api/v1/auth/mfa/challenge` with `{ recoveryCode: "ABCD-1234-EFGH" }` | HTTP 200 with `{ accessToken, user, tenant, permissions }`. Tokens are issued. |
| 3 | Verify the JWT access token contains expected claims (`sub`, `email`, `tenant_id`, `roles`) | Token is valid and well-formed. |
| 4 | Verify the refresh token cookie is set with `httpOnly; Secure; SameSite=Strict` | Cookie is present and secure. |
| 5 | Query `mfa_recovery_code` table for the used code | The row matching the code hash has `used_at` set to the current timestamp. |
| 6 | Verify 9 recovery codes remain unused (`used_at = null`) | Only the submitted code is marked as used. |
| 7 | Verify an `mfa_recovery_code_used` audit event is logged | Audit record contains `user_id`, `tenant_id`, timestamp, and indicates which code index was used. |
| 8 | Verify the response or a subsequent notification prompts the user to regenerate recovery codes or re-enroll MFA | Response includes a flag such as `{ promptRegenerateRecoveryCodes: true }` or a warning message. |
| 9 | Verify MFA remains enabled on the account (`mfa_enabled = true`) | Recovery code login does not disable MFA. |

## 6. Postconditions
- User is fully authenticated with JWT access token and refresh token.
- One recovery code is marked as used; 9 remain.
- Audit log contains `mfa_recovery_code_used` event.
- User is prompted to regenerate recovery codes.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
