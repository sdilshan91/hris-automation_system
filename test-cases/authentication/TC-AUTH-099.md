---
id: TC-AUTH-099
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-099: Lockout state persists across API instance restarts

## 1. Test Objective
Verify that the lockout state (`failed_login_count` and `locked_until`) is stored in the database and persists across API instance restarts (NFR-5). An account that was locked before a restart must remain locked after the restart.

## 2. Related Requirements
- User Story: US-AUTH-010
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- User `alice@acme.com` is locked: `locked_until` is approximately 10 minutes in the future, `failed_login_count = 5`.
- The API server can be restarted during the test.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Locked account |
| Correct password | S3cure!Pass2026 | Valid credential |
| locked_until | now() + 10 minutes | Active lockout |
| failed_login_count | 5 | At threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` is locked: `locked_until` in the future, `failed_login_count = 5`. | Precondition verified. |
| 2 | Send `POST /api/v1/auth/login` with correct password. | HTTP 401 with lockout message -- confirmed locked pre-restart. |
| 3 | Restart the API server instance (stop and start the `HRM.Api` process). | API server restarts successfully. |
| 4 | After restart, query `users.failed_login_count` for `alice@acme.com` directly in the database. | Value is still `5` -- persisted in PostgreSQL. |
| 5 | Query `users.locked_until`. | Value is still the same future timestamp -- persisted in PostgreSQL. |
| 6 | Send `POST /api/v1/auth/login` with correct password after restart. | HTTP 401 with lockout message -- lockout survived the restart. |
| 7 | Wait for `locked_until` to expire. Send `POST /api/v1/auth/login` with correct password. | HTTP 200 OK; login succeeds. The expiry logic also works correctly post-restart. |

## 6. Postconditions
- Lockout state survived the API restart because it is stored in PostgreSQL, not in-memory or Redis-only.
- Normal lockout expiry flow works after restart.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
