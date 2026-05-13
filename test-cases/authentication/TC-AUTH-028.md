---
id: TC-AUTH-028
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-028: Account unlocks after cooldown period

## 1. Test Objective
Verify that after the lockout duration expires, the user can successfully log in with correct credentials, and the system clears the failed login counter and lockout timestamp.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-4, AC-6
- Functional Requirements: FR-2, FR-4
- Business Rules: BR-2

## 3. Preconditions
- User `john@acme.com` was locked out 15+ minutes ago.
- The lockout duration was set to 15 minutes.
- `locked_until` timestamp is now in the past.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Previously locked account |
| Correct Password | S3cure!Pass2026 | Valid password |
| locked_until | (15+ minutes ago) | Lockout has expired |
| failed_login_count | 5 | From the lockout |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify `users.locked_until` is in the past for `john@acme.com` | Lockout period has expired. |
| 2 | Send `POST /api/v1/auth/login` with correct credentials | HTTP 200 OK; login succeeds. |
| 3 | Verify JWT access token is issued with correct claims | Token contains valid `sub`, `tenant_id`, `roles`, `permissions`. |
| 4 | Verify refresh token cookie is set | `httpOnly; Secure; SameSite=Strict` cookie. |
| 5 | Verify `users.failed_login_count` is reset to 0 | Counter is cleared on successful login. |
| 6 | Verify `users.locked_until` is set to null | Lockout timestamp is cleared. |
| 7 | Verify an `account_unlocked_by_timeout` audit event is logged | Audit record contains user_id and timestamp. |
| 8 | Test with wrong password after lockout expiry | HTTP 401; `failed_login_count` is set to 1 (fresh counter starts). |
| 9 | Test admin manual unlock: while account is still locked, admin calls unlock endpoint | `locked_until = null`, `failed_login_count = 0`; `account_unlocked_by_admin` audit event logged with admin user_id. |
| 10 | After admin unlock, verify immediate login is possible | HTTP 200; login succeeds without waiting for lockout to expire. |
| 11 | Test successful login below threshold resets counter: fail 3 times, then succeed | `failed_login_count` resets to 0 on success (per AC-6). |

## 6. Postconditions
- The user's account is unlocked and fully accessible.
- `failed_login_count` is 0 and `locked_until` is null.
- Audit events are recorded for the unlock.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
