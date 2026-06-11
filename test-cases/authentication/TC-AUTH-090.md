---
id: TC-AUTH-090
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-090: MFA failures count toward lockout threshold (shared counter)

## 1. Test Objective
Verify that failed MFA (TOTP) verification attempts increment the same `failed_login_count` counter as password failures, and that the lockout threshold applies to the combined total. A mix of password and MFA failures should trigger lockout when the sum reaches the threshold.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-10
- Dependencies: US-AUTH-005 (MFA)

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `bob@acme.com` has MFA (TOTP) enabled, `failed_login_count = 0`, `locked_until = null`.
- User knows the correct password but will submit invalid TOTP codes.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | bob@acme.com | MFA-enabled user |
| Correct password | S3cure!Pass2026 | Valid |
| Invalid TOTP | 000000 | Wrong code |
| Max failed attempts | 5 | Combined threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with wrong password (attempt 1). | HTTP 401; `failed_login_count` = 1. |
| 2 | Send `POST /api/v1/auth/login` with wrong password (attempt 2). | HTTP 401; `failed_login_count` = 2. |
| 3 | Send `POST /api/v1/auth/login` with CORRECT password for `bob@acme.com`. | HTTP 200 with MFA challenge (partial auth state); `failed_login_count` remains 2 (password succeeded, MFA pending). |
| 4 | Send `POST /api/v1/auth/mfa/verify` with an invalid TOTP code (attempt 3). | HTTP 401; `failed_login_count` = 3. MFA failure increments the counter. |
| 5 | Begin a new login with correct password; receive MFA challenge. | MFA challenge issued. |
| 6 | Send `POST /api/v1/auth/mfa/verify` with an invalid TOTP code (attempt 4). | HTTP 401; `failed_login_count` = 4. |
| 7 | Begin a new login with correct password; receive MFA challenge. | MFA challenge issued. |
| 8 | Send `POST /api/v1/auth/mfa/verify` with an invalid TOTP code (attempt 5 -- threshold). | HTTP 401 with lockout message; `failed_login_count` = 5. |
| 9 | Verify `users.locked_until` is set to approximately `now() + 15 minutes`. | Account is locked. |
| 10 | Verify an `account_locked` audit event is logged with `attempt_count = 5`. | Audit event exists with correct metadata. |
| 11 | Attempt login with correct password while locked. | HTTP 401 with lockout message -- account is locked regardless of credential correctness. |

## 6. Postconditions
- `failed_login_count = 5`; `locked_until` is set.
- The shared counter accumulated both password (2) and MFA (3) failures.
- Account is locked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
