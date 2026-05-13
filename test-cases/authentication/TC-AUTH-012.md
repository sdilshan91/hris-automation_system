---
id: TC-AUTH-012
user_story: US-AUTH-004
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-012: Reset with expired/invalid token fails

## 1. Test Objective
Verify that the password reset endpoint rejects expired, already-used, or invalid tokens with a 400 Bad Request and an appropriate error message.

## 2. Related Requirements
- User Story: US-AUTH-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-4

## 3. Preconditions
- User `john@acme.com` has requested a password reset.
- For expired token test: the reset token was generated more than 1 hour ago.
- For reused token test: the reset token has already been successfully used once.
- For invalid token test: a tampered/fabricated token string.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Email | john@acme.com | Valid user |
| Expired Token | (token generated > 1 hour ago) | Past configurable expiry |
| Used Token | (token already consumed) | Single-use token |
| Invalid Token | abc123-fake-token-xyz | Fabricated string |
| New Password | N3wS3cure!Pass2026 | Valid password |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/reset-password` with an expired token | HTTP 400 Bad Request with message "This reset link has expired. Please request a new one." |
| 2 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 3 | Send `POST /api/v1/auth/reset-password` with a previously used (consumed) token | HTTP 400 Bad Request with message "This reset link has already been used. Please request a new one." |
| 4 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 5 | Send `POST /api/v1/auth/reset-password` with a fabricated/invalid token | HTTP 400 Bad Request with message indicating the token is invalid. |
| 6 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 7 | Send `POST /api/v1/auth/reset-password` with a valid token but wrong email | HTTP 400 Bad Request; token-email mismatch is rejected. |
| 8 | Verify no refresh tokens are revoked in any failed scenario | Token revocation only occurs on successful password reset. |
| 9 | Test password policy violation: send a valid token with a weak password `123` | HTTP 400 Bad Request with validation errors (e.g., "Password must be at least 12 characters"). |
| 10 | Test password history: attempt to reset to a recently used password | HTTP 400 Bad Request with "Password has been used recently. Please choose a different password." |

## 6. Postconditions
- The user's password remains unchanged.
- No tokens have been revoked.
- The user is prompted to request a new reset link (for expired/used tokens) or fix validation errors.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
