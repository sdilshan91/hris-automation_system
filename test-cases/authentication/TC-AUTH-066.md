---
id: TC-AUTH-066
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-066: Concurrent session limit -- revoke_oldest strategy evicts oldest session

## 1. Test Objective
Verify that when a tenant's session policy uses `concurrentSessionStrategy = "revoke_oldest"` and the user is at `maxConcurrentSessions`, the system automatically revokes the oldest session (by `issued_at`), creates the new session, maintains the session count at the limit, and forces the evicted session's device to re-authenticate.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5, FR-9
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is in `active` state.
- Tenant "acme" session policy: `maxConcurrentSessions = 3`, `concurrentSessionStrategy = "revoke_oldest"`.
- User `john@acme.com` has exactly 3 active sessions with distinct `issued_at` timestamps.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | maxConcurrentSessions = 3 |
| Strategy | revoke_oldest | Oldest session is evicted |
| User | john@acme.com | Employee role |
| Session 1 | Chrome/Windows, issued T-3h | Oldest -- should be evicted |
| Session 2 | Safari/macOS, issued T-2h | Middle |
| Session 3 | Firefox/Linux, issued T-1h | Newest existing |
| 4th login device | Edge/Android | New session |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify 3 active sessions exist for the user-tenant pair. | 3 rows with `revoked_at IS NULL` and not expired. |
| 2 | Call `POST /api/v1/auth/login` with valid credentials from a 4th device (Edge/Android). | HTTP 200; new access token and refresh token are issued (Session 4). |
| 3 | Verify Session 1 (oldest by `issued_at`) is now revoked. | Session 1's `revoked_at` is set to the current timestamp. |
| 4 | Verify Sessions 2, 3, and 4 remain active. | All three have `revoked_at IS NULL`. |
| 5 | From Session 1's device, call `POST /api/v1/auth/refresh` with Session 1's refresh token. | HTTP 401 Unauthorized; body indicates the refresh token is revoked. |
| 6 | Verify the frontend on Session 1's device would redirect to login. | Response triggers re-authentication flow. |
| 7 | From Session 2's device, call `POST /api/v1/auth/refresh`. | HTTP 200; refresh succeeds normally. |
| 8 | Log in from a 5th device while Sessions 2, 3, and 4 are active. | HTTP 200; Session 2 (now the oldest) is revoked; Session 5 is created. |
| 9 | Verify total active sessions remain exactly 3 (Sessions 3, 4, 5). | 3 active refresh tokens in the table. |
| 10 | Verify `concurrent_session_oldest_revoked` audit events are logged for both evictions. | Each audit record contains: `event_type`, `revoked_session_id`, `new_session_id`, `user_id`, `tenant_id`. |

## 6. Postconditions
- The oldest session is always evicted when the limit is reached.
- Active session count never exceeds `maxConcurrentSessions`.
- Evicted sessions return 401 on refresh.
- Audit trail contains `concurrent_session_oldest_revoked` events.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
