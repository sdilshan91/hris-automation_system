---
id: TC-AUTH-065
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-065: Concurrent session limit -- deny_new strategy blocks login at limit

## 1. Test Objective
Verify that when a tenant's session policy uses `concurrentSessionStrategy = "deny_new"` and the user has reached `maxConcurrentSessions`, any additional login attempt is rejected with an appropriate error message, no new tokens are issued, and existing sessions remain unaffected.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is in `active` state.
- Tenant "acme" session policy: `maxConcurrentSessions = 3`, `concurrentSessionStrategy = "deny_new"`.
- User `john@acme.com` has the `Employee` role and valid credentials.
- User `john@acme.com` already has exactly 3 active (non-revoked, non-expired) refresh tokens from 3 different devices.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | acme | maxConcurrentSessions = 3 |
| Strategy | deny_new | New logins are blocked |
| User | john@acme.com | Employee role |
| Session 1 | Chrome/Windows, issued T-2h | Active |
| Session 2 | Safari/macOS, issued T-1h | Active |
| Session 3 | Firefox/Linux, issued T-30m | Active |
| 4th login device | Edge/Android | Should be denied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Query `refresh_token` table for user-tenant pair. | Exactly 3 records with `revoked_at IS NULL` and not expired. |
| 2 | Call `POST /api/v1/auth/login` with valid credentials from a 4th device (Edge/Android). | HTTP 409 Conflict is returned. |
| 3 | Inspect the error response body. | Body contains `"code": "CONCURRENT_SESSION_LIMIT"` and `"message": "Maximum concurrent sessions reached. Please log out from another device or contact your administrator."` |
| 4 | Verify no new tokens were created. | No new `refresh_token` row exists; no access token in the response. |
| 5 | Verify all 3 existing sessions remain active. | All 3 refresh tokens still have `revoked_at IS NULL`. |
| 6 | Verify a `concurrent_session_denied` audit event is logged. | Audit log contains: `event_type = "concurrent_session_denied"`, `user_id`, `tenant_id`, `active_session_count = 3`, `strategy = "deny_new"`, `ip_address` of the denied attempt, `user_agent` of the denied attempt. |
| 7 | Log out from Session 1 (Chrome/Windows) via `POST /api/v1/auth/logout`. | Session 1 is revoked; active count drops to 2. |
| 8 | Retry login from the 4th device (Edge/Android). | HTTP 200; login succeeds; new access token and refresh token are issued. |
| 9 | Verify active session count is now 3 (Sessions 2, 3, and the new Session 4). | 3 active refresh tokens in the table. |

## 6. Postconditions
- The 4th login was blocked when at the limit, then succeeded after one session was freed.
- Audit log contains the `concurrent_session_denied` event for the blocked attempt.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
