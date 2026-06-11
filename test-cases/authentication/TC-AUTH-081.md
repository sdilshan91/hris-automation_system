---
id: TC-AUTH-081
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-081: Idle timeout is reset by any authenticated API request (BR-6)

## 1. Test Objective
Verify that the idle timeout timer is reset by any authenticated API request -- not just token refresh requests -- per BR-6. The `last_active_at` timestamp is updated (debounced per FR-4) on regular API calls, preventing idle expiry for active users.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" has `idleTimeoutMinutes = 5` (short value for testing).
- User `jane@acme.com` has an active session.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | idleTimeoutMinutes = 5 |
| User | jane@acme.com | Employee role |
| Regular API call | GET /api/v1/auth/me | Not a refresh call |
| Refresh call | POST /api/v1/auth/refresh | Token refresh |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in as `jane@acme.com`. Note `last_active_at` timestamp. | Session created; `last_active_at` is set. |
| 2 | Wait 3 minutes (within idle timeout). | No activity. |
| 3 | Make a regular API call: `GET /api/v1/auth/me`. | HTTP 200; the request succeeds. |
| 4 | Check `last_active_at` in the database. | `last_active_at` has been updated to approximately the current time (within debounce interval). |
| 5 | Wait another 3 minutes. | Total 6 minutes since login, but only 3 minutes since last activity. |
| 6 | Call `POST /api/v1/auth/refresh`. | HTTP 200; refresh succeeds because `last_active_at` was reset in step 3 (only 3 minutes of idle time). |
| 7 | Wait 6 minutes without any API requests (exceeding the 5-minute idle timeout). | No activity. |
| 8 | Call `POST /api/v1/auth/refresh`. | HTTP 401; idle timeout exceeded. |
| 9 | **Debounce verification:** Make 10 rapid API calls within 30 seconds. | All calls succeed. |
| 10 | Check the database `last_active_at` update count for this session. | `last_active_at` was updated at most once (per the debounce interval of approximately 1 minute per FR-4). |
| 11 | **Non-authenticated endpoint:** Access a public endpoint (e.g., health check). | The idle timer is NOT reset (only authenticated API requests reset it). |

## 6. Postconditions
- Idle timeout is correctly reset by authenticated API activity.
- Debouncing prevents excessive database writes.
- Non-authenticated requests do not affect the idle timer.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
