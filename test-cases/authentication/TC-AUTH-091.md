---
id: TC-AUTH-091
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-091: Password reset clears lockout state

## 1. Test Objective
Verify that completing a password reset (US-AUTH-004) clears both `failed_login_count` and `locked_until`, effectively unlocking the account even if the lockout period has not yet expired.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-2
- Dependencies: US-AUTH-004 (Password reset)
- Functional Requirements: FR-4

## 3. Preconditions
- User `alice@acme.com` is locked: `locked_until` is 10 minutes in the future, `failed_login_count = 5`.
- The password reset flow (US-AUTH-004) is functional.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Locked account |
| New password | N3wS3cure!Pass2026 | Replacement password |
| locked_until | now() + 10 minutes | Active lockout |
| failed_login_count | 5 | At threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` is locked: `locked_until` in future, `failed_login_count = 5`. | Precondition verified. |
| 2 | Send `POST /api/v1/auth/forgot-password` with `alice@acme.com`. | HTTP 200; password reset email is sent. |
| 3 | Extract the reset token from the email (or database for test purposes). | Valid reset token obtained. |
| 4 | Send `POST /api/v1/auth/reset-password` with the reset token and a new password. | HTTP 200 OK; password is reset. |
| 5 | Query `users.failed_login_count` for `alice@acme.com`. | Value is `0` -- cleared by password reset. |
| 6 | Query `users.locked_until` for `alice@acme.com`. | Value is `null` -- cleared by password reset. |
| 7 | Immediately send `POST /api/v1/auth/login` with the NEW password. | HTTP 200 OK; login succeeds even though the original lockout period has not expired. |
| 8 | Verify JWT and refresh token are issued normally. | Valid tokens present. |
| 9 | Attempt login with the OLD password. | HTTP 401 with "Invalid email or password"; `failed_login_count` = 1 (fresh counter). |

## 6. Postconditions
- Account is fully unlocked via password reset.
- `failed_login_count = 0`, `locked_until = null`.
- User can log in with the new password.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
