---
id: TC-AUTH-025
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-025: Oldest session terminated when limit exceeded

## 1. Test Objective
Verify that when the "revoke_oldest" concurrent session strategy is active and the session limit is exceeded, the system correctly identifies and revokes the oldest session, and the affected user's device is forced to re-authenticate on its next refresh attempt.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1, AC-5
- Functional Requirements: FR-1, FR-5, FR-9

## 3. Preconditions
- Tenant "acme" has `maxConcurrentSessions = 2` and `concurrentSessionStrategy = "revoke_oldest"`.
- User `john@acme.com` has 2 active sessions:
  - Session 1: issued at T1 (oldest), device: Chrome on Windows
  - Session 2: issued at T2, device: Safari on MacOS

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Has 2 active sessions |
| Tenant | acme | maxConcurrentSessions = 2 |
| Session 1 | Chrome/Windows, issued at T1 | Oldest session |
| Session 2 | Safari/MacOS, issued at T2 | Newer session |
| New login | Firefox/Linux | 3rd device |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Sessions 1 and 2 are active in the `refresh_token` table | Both have `revoked_at = null`. |
| 2 | Log in from a 3rd device (Firefox/Linux) | HTTP 200; new JWT and refresh token issued (Session 3). |
| 3 | Verify Session 1 (oldest by `issued_at`) is now revoked | Session 1 has `revoked_at` set to the current timestamp. |
| 4 | Verify Sessions 2 and 3 remain active | Both have `revoked_at = null`. |
| 5 | From Session 1's device (Chrome/Windows), attempt `POST /api/v1/auth/refresh` | HTTP 401 Unauthorized; refresh token is revoked. |
| 6 | Verify the frontend on Session 1's device redirects to the login page | "Session expired" toast notification is displayed. |
| 7 | Verify Sessions 2 and 3 can still refresh successfully | Both sessions continue to work normally. |
| 8 | View active sessions via `GET /api/v1/auth/me/sessions` from Session 2 | Shows Session 2 (current) and Session 3; Session 1 is no longer listed. |
| 9 | Verify audit events are logged | `concurrent_session_oldest_revoked` event with the revoked session details. |

## 6. Postconditions
- The oldest session is revoked and the user on that device must re-authenticate.
- The two remaining sessions (newer + newly created) are active.
- The session count remains at the configured limit.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
