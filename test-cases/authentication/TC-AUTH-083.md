---
id: TC-AUTH-083
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-083: Failed login increment below threshold returns generic 401 with no remaining-count leak

## 1. Test Objective
Verify that each failed login attempt below the lockout threshold increments `failed_login_count` by exactly 1, returns a generic 401 response with the message "Invalid email or password," and does not leak the remaining attempts count in the response body, headers, or any other channel.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-3
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" has default lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `alice@acme.com` exists with `failed_login_count = 0` and `locked_until = null`.
- User has a known correct password for verification reference only.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | acme | Active tenant |
| User email | alice@acme.com | Active user, no prior failures |
| Wrong password | Wr0ngP@ss1 | Incorrect credential |
| Max failed attempts | 5 | Tenant policy default |
| Initial failed_login_count | 0 | Clean state |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with `alice@acme.com` and wrong password (attempt 1). | HTTP 401; response body contains `"Invalid email or password"`. |
| 2 | Query `users.failed_login_count` for `alice@acme.com` in the database. | Value is `1`. |
| 3 | Inspect the entire HTTP response (body, headers) for attempt 1. | No field, header, or value reveals the remaining attempts count (e.g., no `X-Remaining-Attempts`, no `remainingAttempts` in JSON, no `attemptsLeft`). |
| 4 | Send `POST /api/v1/auth/login` with wrong password (attempt 2). | HTTP 401; same generic message `"Invalid email or password"`. |
| 5 | Query `users.failed_login_count`. | Value is `2`. |
| 6 | Send `POST /api/v1/auth/login` with wrong password (attempt 3). | HTTP 401; same generic message. |
| 7 | Query `users.failed_login_count`. | Value is `3`. |
| 8 | Send `POST /api/v1/auth/login` with wrong password (attempt 4). | HTTP 401; same generic message `"Invalid email or password"` (NOT the lockout message, since threshold is 5). |
| 9 | Query `users.failed_login_count`. | Value is `4`. |
| 10 | Verify `users.locked_until` remains `null` after 4 failures. | Account is NOT locked; `locked_until` is null. |
| 11 | Verify all four 401 responses are identical in structure (same JSON shape, same message text). | No variation in the error message or structure that would reveal attempt count. |
| 12 | Verify a `login_failure` audit event is logged for each of the 4 attempts, each including `attempt_count`. | Four `login_failure` audit records exist, with attempt counts 1 through 4. |

## 6. Postconditions
- `failed_login_count` is 4; `locked_until` is null.
- The user can still attempt to log in.
- Four `login_failure` audit events are recorded.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
