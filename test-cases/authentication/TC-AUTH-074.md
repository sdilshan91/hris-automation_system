---
id: TC-AUTH-074
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-074: Boundary -- exactly at maxConcurrentSessions and timeout at exact threshold

## 1. Test Objective
Verify correct system behavior at precise boundary conditions: (a) login when session count is exactly one below the limit succeeds, (b) login when session count is exactly at the limit is handled per strategy, (c) token refresh at exactly the idle timeout threshold succeeds, (d) token refresh 1 second after the idle timeout threshold fails, (e) refresh at exactly the absolute timeout threshold succeeds, and (f) refresh 1 second after fails.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1, AC-2, AC-3
- Functional Requirements: FR-1, FR-2, FR-3, FR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" session policy: `maxConcurrentSessions = 3`, `concurrentSessionStrategy = "deny_new"`, `idleTimeoutMinutes = 30`, `absoluteTimeoutHours = 12`.
- User `john@acme.com` is available for testing.
- Test infrastructure supports precise timestamp manipulation or database-level `last_active_at`/`issued_at` adjustments.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | maxConcurrentSessions = 3 |
| Strategy | deny_new | For concurrent session boundary tests |
| idleTimeoutMinutes | 30 | 1800 seconds |
| absoluteTimeoutHours | 12 | 43200 seconds |
| User | john@acme.com | Test subject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Concurrent boundary -- below limit:** Ensure john has exactly 2 active sessions. Login from a 3rd device. | HTTP 200; login succeeds. Active session count is now 3 (at the limit). |
| 2 | **Concurrent boundary -- at limit:** With 3 active sessions (deny_new), login from a 4th device. | HTTP 409; login is denied with "Maximum concurrent sessions reached." |
| 3 | **Concurrent boundary -- maxConcurrentSessions = 1:** Update policy to `maxConcurrentSessions = 1`, `strategy = "deny_new"`. User has 1 active session. Login from a 2nd device. | HTTP 409; denied. |
| 4 | **Concurrent boundary -- maxConcurrentSessions = 1, revoke_oldest:** Change strategy to `revoke_oldest`. Login from 2nd device. | HTTP 200; the single existing session is revoked; new session is created. Active count = 1. |
| 5 | **Idle boundary -- at threshold:** Set `last_active_at` for the session to exactly 30 minutes ago (now - 1800s). Call `POST /api/v1/auth/refresh`. | HTTP 200; refresh succeeds (the idle timeout has not been exceeded, only reached). OR HTTP 401 if the system treats the boundary as "expired" -- document the actual behavior. |
| 6 | **Idle boundary -- 1 second past threshold:** Set `last_active_at` to now - 1801s. Call `POST /api/v1/auth/refresh`. | HTTP 401; session is idle-expired. |
| 7 | **Idle boundary -- 1 second before threshold:** Set `last_active_at` to now - 1799s. Call `POST /api/v1/auth/refresh`. | HTTP 200; refresh succeeds. |
| 8 | **Absolute boundary -- at threshold:** Set `issued_at` to exactly 12 hours ago. Call `POST /api/v1/auth/refresh`. | HTTP 200 or HTTP 401 -- document the boundary behavior (inclusive vs exclusive). |
| 9 | **Absolute boundary -- 1 second past threshold:** Set `issued_at` to 12 hours + 1 second ago. Call `POST /api/v1/auth/refresh`. | HTTP 401; session is absolute-expired. |
| 10 | **Absolute boundary -- 1 second before threshold:** Set `issued_at` to 12 hours - 1 second ago. Call `POST /api/v1/auth/refresh`. | HTTP 200; refresh succeeds. |
| 11 | **Idle timeout with debounce consideration:** Note that per the assumptions, idle timeout is approximate within +/- 1 minute due to debounced `last_active_at` updates. Verify that a session with `last_active_at` = 29 minutes ago (1 minute before threshold) is NOT expired. | HTTP 200; session is still valid. |

## 6. Postconditions
- Boundary conditions are clearly documented (whether thresholds are inclusive or exclusive).
- System behavior is consistent and predictable at the exact limits.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
