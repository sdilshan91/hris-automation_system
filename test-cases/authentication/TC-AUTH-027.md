---
id: TC-AUTH-027
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-027: Locked account cannot login

## 1. Test Objective
Verify that a locked account cannot log in even with correct credentials until the lockout period expires, and that the system checks lockout status before verifying the password.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5

## 3. Preconditions
- User `john@acme.com` has been locked out (`locked_until` is set to a future timestamp, approximately 15 minutes from lockout time).
- The lockout period has NOT yet expired.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Locked account |
| Correct Password | S3cure!Pass2026 | Valid password |
| locked_until | (current time + 10 minutes) | Still within lockout window |
| Expected HTTP Status | 401 | Unauthorized |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify `users.locked_until` is in the future for `john@acme.com` | Account is currently locked. |
| 2 | Send `POST /api/v1/auth/login` with the CORRECT password | HTTP 401 with "Account temporarily locked. Try again later or contact your administrator." |
| 3 | Verify the system checks `locked_until` BEFORE verifying the password | Password hash comparison does not occur (lockout check is first). |
| 4 | Verify no JWT or refresh token is issued | No tokens in the response. |
| 5 | Verify the `failed_login_count` is NOT further incremented | Counter remains at the lockout threshold value. |
| 6 | Compare response time with a login attempt for a non-existent email | Response times are similar (resistant to timing attacks per NFR-4). |
| 7 | Verify the error message does not reveal that the password was correct | Message is the same generic lockout message regardless of password validity. |
| 8 | Verify existing active sessions (refresh tokens) for this user remain valid | Lockout only affects new logins, not existing sessions (per BR-7). |

## 6. Postconditions
- The user remains locked out.
- No new tokens are issued.
- Existing sessions continue to function.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
