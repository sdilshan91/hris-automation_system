---
id: TC-AUTH-086
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-086: Lockout expiry clears counters and allows successful login

## 1. Test Objective
Verify that after the lockout duration expires, a login with correct credentials succeeds, the system clears both `locked_until` and `failed_login_count`, issues tokens normally, and logs an `account_unlocked_by_timeout` audit event.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-4
- Data Requirements: `account_unlocked_by_timeout` audit record

## 3. Preconditions
- User `alice@acme.com` was locked out; `locked_until` is now in the past (lockout has expired).
- `failed_login_count = 5` from the previous lockout cycle.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Previously locked |
| Correct password | S3cure!Pass2026 | Valid credential |
| locked_until | now() - 1 minute | Expired lockout |
| failed_login_count | 5 | From prior lockout |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` has `locked_until` in the past and `failed_login_count = 5`. | Precondition verified. |
| 2 | Send `POST /api/v1/auth/login` with correct credentials. | HTTP 200 OK; login succeeds. |
| 3 | Verify a JWT access token is returned with correct claims (`sub`, `tenant_id`, `roles`, `permissions`). | Valid JWT is present in the response. |
| 4 | Verify a refresh token cookie is set (`httpOnly; Secure; SameSite=Strict`). | Cookie is present with correct flags. |
| 5 | Query `users.failed_login_count` for `alice@acme.com`. | Value is `0` -- cleared on successful login. |
| 6 | Query `users.locked_until` for `alice@acme.com`. | Value is `null` -- cleared on successful login. |
| 7 | Query the audit log for an `account_unlocked_by_timeout` event for `alice@acme.com`. | Event exists with `user_id` and timestamp. |
| 8 | Send `POST /api/v1/auth/login` with wrong password (first failure after unlock). | HTTP 401 with `"Invalid email or password"`; `failed_login_count` is `1` (fresh counter). |

## 6. Postconditions
- `failed_login_count` is 0 (or 1 if step 8 was executed); `locked_until` is null.
- User is fully unlocked and operational.
- `account_unlocked_by_timeout` audit event is recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
