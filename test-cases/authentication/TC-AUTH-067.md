---
id: TC-AUTH-067
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-067: Idle timeout expires session and returns 401 on refresh

## 1. Test Objective
Verify that when a user's session has been idle (no authenticated API requests) beyond the tenant's configured `idleTimeoutMinutes`, the next token refresh attempt is rejected with HTTP 401, the refresh token is revoked, and the user is forced to re-authenticate.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2, FR-4, FR-9
- Business Rules: BR-1, BR-6

## 3. Preconditions
- Tenant "acme" has session policy: `idleTimeoutMinutes = 2` (short value for testing).
- User `jane@acme.com` has an active session with a known `last_active_at` timestamp.
- The access token's lifetime is shorter than the idle timeout, so a refresh will be needed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | idleTimeoutMinutes = 2 |
| User | jane@acme.com | Has 1 active session |
| Idle wait | 3 minutes | Exceeds 2 min timeout |
| last_active_at (before wait) | T-0 | Timestamp at last API call |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the session's `last_active_at` is current (within the last minute). | Confirmed; session is active and not idle. |
| 2 | Make an authenticated API request (e.g., `GET /api/v1/auth/me`). | HTTP 200; `last_active_at` is updated (idle timer reset per BR-6). |
| 3 | Wait 3 minutes without making any authenticated API requests. | No activity; session becomes idle. |
| 4 | Call `POST /api/v1/auth/refresh` with the session's refresh token. | HTTP 401 Unauthorized. |
| 5 | Inspect the error response body. | Body contains `"code": "SESSION_IDLE_EXPIRED"` and a message indicating the session expired due to inactivity. |
| 6 | Verify the refresh token is revoked in the database. | `revoked_at` is set on the refresh token row. |
| 7 | Verify a `session_expired_idle` audit event is logged. | Audit record contains: `event_type = "session_expired_idle"`, `user_id`, `tenant_id`, `session_id`, `idle_duration`. |
| 8 | Attempt to use the old access token for an API call (if not yet expired). | The access token may still be valid until its JWT expiry, but the session is logically terminated. |
| 9 | Log in again with valid credentials. | HTTP 200; new session is created. |

## 6. Postconditions
- The idle-expired session's refresh token is revoked.
- The user must re-authenticate to obtain a new session.
- Audit log records the idle expiration event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
