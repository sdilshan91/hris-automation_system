---
id: TC-AUTH-068
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: draft
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
