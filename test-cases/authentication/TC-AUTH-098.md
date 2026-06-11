---
id: TC-AUTH-098
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-098: Atomic increment of failed_login_count under concurrent login attempts

## 1. Test Objective
Verify that the `failed_login_count` update is atomic at the database level (NFR-2) when multiple concurrent login attempts with wrong passwords are fired simultaneously. No race condition should occur -- the final counter value must exactly equal the number of failed attempts.

## 2. Related Requirements
- User Story: US-AUTH-010
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1

## 3. Preconditions
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`.
- Tenant "acme" has `maxFailedAttempts = 5`.
- The test harness can fire concurrent HTTP requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user |
| Wrong password | Wr0ngP@ss | Incorrect |
| Concurrent requests | 10 | Simultaneously fired |
| Max failed attempts | 5 | Lockout threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` has `failed_login_count = 0`. | Precondition verified. |
| 2 | Simultaneously fire 10 `POST /api/v1/auth/login` requests with wrong password from 10 parallel threads/connections. | All 10 requests complete (mix of 401 responses). |
| 3 | Wait for all responses to return. | All 10 responses received. |
| 4 | Query `users.failed_login_count` for `alice@acme.com`. | Value is exactly `10` (if threshold allows) OR at least 5 if lockout fires mid-batch. The key assertion: the value must be exactly equal to the number of failed attempts processed, with no lost increments. |
| 5 | Verify that the `locked_until` is set (since 10 > 5 threshold). | Account is locked after the 5th concurrent failure was processed. |
| 6 | Count `login_failure` audit events for `alice@acme.com`. | Exactly 10 `login_failure` audit events (or at least 5 before lockout, with subsequent attempts logged differently). Each has a unique timestamp or request ID. |
| 7 | Reset `alice@acme.com`: set `failed_login_count = 0`, `locked_until = null`. Fire exactly 4 concurrent requests. | All 4 responses return HTTP 401. |
| 8 | Query `users.failed_login_count`. | Exactly `4` -- no lost or duplicated increments under concurrency. |
| 9 | Verify `locked_until` is still `null` (4 < 5 threshold). | Account is NOT locked. |

## 6. Postconditions
- Counter accurately reflects the number of failed attempts without race conditions.
- Atomic database-level increment is confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
