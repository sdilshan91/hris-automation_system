---
id: TC-AUTH-095
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-095: Lockout does NOT revoke active sessions (existing refresh tokens remain valid)

## 1. Test Objective
Verify that when an account is locked out, already-active sessions (existing refresh tokens) remain valid and functional (BR-7). Lockout only prevents NEW logins; it does not terminate existing sessions.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-7

## 3. Preconditions
- User `alice@acme.com` has an active session (valid refresh token) obtained before any failed attempts.
- `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user with existing session |
| Existing refresh token | (obtained from prior login) | Valid token |
| Wrong password | Wr0ngP@ss | For lockout triggering |
| Max failed attempts | 5 | Tenant policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in as `alice@acme.com` and obtain a valid JWT + refresh token. Store the refresh token. | Tokens obtained. |
| 2 | Use the JWT to call an authenticated endpoint (e.g., `GET /api/v1/auth/me`). | HTTP 200 OK; session is active. |
| 3 | From a different client/session, fail login 5 times with wrong password to trigger lockout. | `failed_login_count = 5`; `locked_until` is set. Account is locked. |
| 4 | Using the ORIGINAL session's JWT (from step 1), call `GET /api/v1/auth/me`. | HTTP 200 OK; existing session still works. |
| 5 | Using the original session's refresh token, call `POST /api/v1/auth/refresh`. | HTTP 200 OK; new JWT is issued. The refresh token rotation succeeds. |
| 6 | Use the newly refreshed JWT to call an authenticated endpoint. | HTTP 200 OK; the session continues to function. |
| 7 | Attempt a NEW login as `alice@acme.com` with correct password. | HTTP 401 with lockout message -- new login is blocked. |
| 8 | Verify the existing session from steps 1-6 is STILL functional after the blocked new-login attempt. | Session is unaffected. |

## 6. Postconditions
- The pre-existing session remains fully functional throughout and after the lockout.
- New login attempts are blocked by lockout.
- No refresh tokens were revoked by the lockout mechanism.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
