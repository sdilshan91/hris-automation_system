---
id: TC-AUTH-068
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: blocked
exec_note: "2026-07-01 BLOCKED: absolute-timeout test needs (a) tenant absoluteTimeoutHours set low + (b) DB-level manipulation of session issued_at to simulate 61+ min elapsed (or a 60-min real wait). psql access denied (no credential guessing) and can't wait 60min in a breadth run; refresh token is HTTP-only-cookie based (not body-settable). auth-settings shows absoluteTimeoutHours=8. Re-run in a time-manipulable session harness with DB access. | 2026-07-03 STILL BLOCKED (BUG-240 fix partially helps but NOT sufficient): PUT /tenant/auth-settings absoluteTimeoutHours is NOW writable (BUG-240 fixed) — but the field's unit is HOURS with a floor of 1 (cannot set sub-hour), so exercising the timeout still requires EITHER a 60+ min real wait OR DB manipulation of the session issued_at, plus the refresh token is httpOnly-cookie-based (not body-settable). BUG-240 did not provide the time-travel/DB harness that is the true blocker. Re-run in a time-manipulable session harness with DB access."
created: 2026-06-11
---

# TC-AUTH-068: Absolute timeout forces re-authentication regardless of activity

## 1. Test Objective
Verify that when a session has been active for longer than the tenant's configured `absoluteTimeoutHours`, the system revokes the refresh token and returns HTTP 401 on the next refresh attempt, forcing re-authentication -- even if the user has been continuously active.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-3, FR-9
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" has session policy: `absoluteTimeoutHours = 1` (short value for testing).
- User `jane@acme.com` has an active session with `issued_at` known.
- The user has been making regular API requests throughout (session is NOT idle).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | absoluteTimeoutHours = 1 |
| User | jane@acme.com | Continuously active session |
| Session issued_at | T-0 | Start of the session |
| Test wait | > 60 minutes | Exceeds absolute timeout |
| last_active_at | Recent (within 1 min) | User has been active |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in and note the session's `issued_at` timestamp. | Session created with `issued_at = T-0`. |
| 2 | Make periodic authenticated API requests over 60+ minutes to keep the session non-idle. | Each request succeeds; `last_active_at` is updated. |
| 3 | After 61 minutes from `issued_at`, call `POST /api/v1/auth/refresh`. | HTTP 401 Unauthorized. |
| 4 | Inspect the error response body. | Body contains `"code": "SESSION_ABSOLUTE_EXPIRED"` and a message indicating the session has exceeded the maximum duration. |
| 5 | Verify the refresh token is revoked in the database. | `revoked_at` is set. |
| 6 | Verify `last_active_at` was recently updated (within the last few minutes). | Confirms the session was NOT idle -- the absolute timeout applies regardless. |
| 7 | Verify a `session_expired_absolute` audit event is logged. | Audit record contains: `event_type = "session_expired_absolute"`, `user_id`, `tenant_id`, `session_id`, `session_duration_hours`. |
| 8 | Log in again with valid credentials. | HTTP 200; new session is created with a fresh `issued_at`. |

## 6. Postconditions
- The absolute-expired session is revoked.
- Activity does not prevent absolute timeout.
- A new login is required.
- Audit log records the absolute expiration event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
