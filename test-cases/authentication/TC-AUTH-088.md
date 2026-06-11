---
id: TC-AUTH-088
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-088: Successful login below threshold resets failed_login_count to zero

## 1. Test Objective
Verify that when a user successfully logs in after one or more failed attempts (but below the lockout threshold), the system resets `failed_login_count` to 0, clears any partial failure history, and issues tokens normally.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-6
- Functional Requirements: FR-1, FR-4

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`.
- User `alice@acme.com` has `failed_login_count = 0` and `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user |
| Correct password | S3cure!Pass2026 | Valid credential |
| Wrong password | Wr0ngP@ss1 | Incorrect credential |
| Max failed attempts | 5 | Threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with wrong password (attempt 1). | HTTP 401; `failed_login_count` = 1. |
| 2 | Send `POST /api/v1/auth/login` with wrong password (attempt 2). | HTTP 401; `failed_login_count` = 2. |
| 3 | Send `POST /api/v1/auth/login` with wrong password (attempt 3). | HTTP 401; `failed_login_count` = 3. |
| 4 | Send `POST /api/v1/auth/login` with the CORRECT password. | HTTP 200 OK; login succeeds. |
| 5 | Verify JWT access token is returned with correct claims. | Valid token present. |
| 6 | Verify refresh token cookie is set. | Cookie present. |
| 7 | Query `users.failed_login_count` for `alice@acme.com`. | Value is `0` -- completely reset on success. |
| 8 | Query `users.locked_until` for `alice@acme.com`. | Value is `null`. |
| 9 | Now fail 4 more times (attempts 1-4 on the new counter). | `failed_login_count` increments from 1 to 4 normally. |
| 10 | Verify the user is NOT locked out after the 4th new failure. | `locked_until` is still `null` -- the previous 3 failures do not carry over. |
| 11 | Log in successfully again. | HTTP 200 OK; `failed_login_count` resets to 0 again. |

## 6. Postconditions
- `failed_login_count` is 0; `locked_until` is null.
- Previous failure history is fully cleared.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
