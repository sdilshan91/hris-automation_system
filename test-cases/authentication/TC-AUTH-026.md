---
id: TC-AUTH-026
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-026: Account locked after N failed attempts

## 1. Test Objective
Verify that after the configured number of consecutive failed login attempts, the user's account is automatically locked for the configured duration, and the appropriate audit events and notifications are generated.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-7, FR-8

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `john@acme.com` has `failed_login_count = 0` and `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Active user |
| Correct Password | S3cure!Pass2026 | For reference |
| Wrong Password | Wr0ngP@ss{1-5} | Used for each failure |
| Max Failed Attempts | 5 | Tenant policy |
| Lockout Duration | 15 minutes | Tenant policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with wrong password (attempt 1) | HTTP 401 with "Invalid email or password"; `failed_login_count` = 1. |
| 2 | Send login with wrong password (attempt 2) | HTTP 401; `failed_login_count` = 2. |
| 3 | Send login with wrong password (attempt 3) | HTTP 401; `failed_login_count` = 3. |
| 4 | Send login with wrong password (attempt 4) | HTTP 401; `failed_login_count` = 4. |
| 5 | Verify response does NOT reveal the remaining attempts count | No "X attempts remaining" in the response. |
| 6 | Send login with wrong password (attempt 5 -- the threshold) | HTTP 401 with "Account temporarily locked. Try again later or contact your administrator." |
| 7 | Verify `users.locked_until` is set to approximately current time + 15 minutes | Timestamp is in the future, approximately 15 minutes from now. |
| 8 | Verify `users.failed_login_count` = 5 | Counter reflects all failures. |
| 9 | Verify an `account_locked` audit event is logged | Audit record contains user_id, IP address, attempt count, lockout_until. |
| 10 | Verify a lockout notification email is sent to the user | Email arrives within 60 seconds with lockout instructions. |
| 11 | Verify the `failed_login_count` update is atomic (no race conditions) | Fire concurrent login requests with wrong passwords; verify counter is accurate. |

## 6. Postconditions
- The user's account is locked for 15 minutes.
- `failed_login_count` is 5 and `locked_until` is set.
- An `account_locked` audit event is recorded.
- A lockout notification email has been sent.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
