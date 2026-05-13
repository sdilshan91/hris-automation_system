---
id: TC-AUTH-014
user_story: US-AUTH-005
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-014: Login with valid TOTP code

## 1. Test Objective
Verify that a user with MFA enabled can complete the two-step login flow: password authentication followed by TOTP code verification, resulting in token issuance.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-4
- User Story: US-AUTH-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2, FR-3, FR-10

## 3. Preconditions
- User `john@acme.com` has MFA enabled (`mfa_enabled = true`) with a valid TOTP secret.
- The user has valid credentials and an active membership in tenant "acme".
- User has access to their authenticator app to generate a current TOTP code.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Email | john@acme.com | MFA-enabled user |
| Password | S3cure!Pass2026 | Correct password |
| TOTP Code | (generated from authenticator) | 6-digit, current time step |
| Time step tolerance | +/- 1 step (30 seconds) | Drift tolerance per FR-10 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with `{ email: "john@acme.com", password: "S3cure!Pass2026" }` | HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }` and NO access/refresh tokens. |
| 2 | Verify no `accessToken` in the response body | Token is absent or null. |
| 3 | Verify no refresh token cookie is set | No `Set-Cookie` header for refresh token. |
| 4 | Verify the frontend displays the TOTP code input (6-digit field) | MFA challenge card is shown with a 6-digit input. |
| 5 | Generate a valid TOTP code from the authenticator app | 6-digit code for the current time step. |
| 6 | Send `POST /api/v1/auth/mfa/verify` with `{ code: "{valid_code}" }` | HTTP 200 with `{ accessToken, user, tenant, permissions }`. |
| 7 | Verify the JWT access token contains all expected claims | Claims include `sub`, `email`, `tenant_id`, `roles`, `permissions`, etc. |
| 8 | Verify the refresh token cookie is now set | `httpOnly; Secure; SameSite=Strict` cookie present. |
| 9 | Verify user is redirected to the dashboard | Navigation to `https://acme.yourhrm.com/dashboard`. |
| 10 | Test time drift tolerance: generate a code for the previous time step (-30 seconds) | Code is accepted (within +/- 1 step window). |
| 11 | Test time drift tolerance: generate a code for the next time step (+30 seconds) | Code is accepted (within +/- 1 step window). |
| 12 | Verify an `mfa_challenge_success` audit event is logged | Audit record contains user_id, tenant_id, timestamp. |

## 6. Postconditions
- User is fully authenticated with JWT access token and refresh token.
- MFA challenge is completed successfully.
- Audit events for both login and MFA verification are recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
