---
id: TC-AUTH-085
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-085: Correct credentials during active lockout are still rejected

## 1. Test Objective
Verify that when a user's account is locked (`locked_until` is in the future), submitting the correct password still results in a 401 rejection with the lockout message. The system must check the lockout status before verifying the password (FR-5), and the `failed_login_count` must NOT be further incremented.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- User `alice@acme.com` is locked: `locked_until` is approximately 10 minutes in the future, `failed_login_count = 5`.
- The user's correct password is known for this test.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Locked account |
| Correct password | S3cure!Pass2026 | Valid credential |
| locked_until | now() + 10 minutes | Active lockout |
| failed_login_count | 5 | At threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` has `locked_until` in the future and `failed_login_count = 5`. | Precondition verified. |
| 2 | Send `POST /api/v1/auth/login` with the CORRECT password. | HTTP 401 with `"Account temporarily locked. Try again later or contact your administrator."` |
| 3 | Verify no JWT access token is present in the response. | No `accessToken` field or it is absent. |
| 4 | Verify no refresh token cookie is set. | No `Set-Cookie` header with refresh token. |
| 5 | Query `users.failed_login_count` for `alice@acme.com`. | Value remains `5` -- NOT incremented to 6. |
| 6 | Query `users.locked_until` for `alice@acme.com`. | Value is unchanged from precondition. |
| 7 | Send `POST /api/v1/auth/login` with an INCORRECT password while still locked. | HTTP 401 with the same lockout message. |
| 8 | Query `users.failed_login_count`. | Still `5` -- lockout check occurs before password verification, so wrong password during lockout does not increment. |
| 9 | Verify the error message is identical for correct and incorrect passwords during lockout (step 2 vs step 7). | Responses are structurally identical -- no information leakage about password correctness. |

## 6. Postconditions
- The user remains locked with `failed_login_count = 5` and `locked_until` unchanged.
- No tokens were issued.
- No additional audit events for failed password verification (only lockout-rejection events if applicable).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
