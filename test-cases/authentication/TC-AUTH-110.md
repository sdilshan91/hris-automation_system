---
id: TC-AUTH-110
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-110: MFA-only failures trigger lockout (5 consecutive invalid TOTP codes)

## 1. Test Objective
Verify that 5 consecutive failed MFA (TOTP) verification attempts -- without any password failures -- are sufficient to trigger account lockout. This confirms FR-10: MFA failures count toward the lockout threshold independently.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-10
- Dependencies: US-AUTH-005 (MFA)

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `bob@acme.com` has MFA enabled, `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | bob@acme.com | MFA-enabled |
| Correct password | S3cure!Pass2026 | Valid |
| Invalid TOTP | 000000, 111111, etc. | Wrong codes |
| Max failed attempts | 5 | Threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with correct password. | HTTP 200 with MFA challenge. `failed_login_count` remains 0. |
| 2 | Send `POST /api/v1/auth/mfa/verify` with invalid TOTP (attempt 1). | HTTP 401; `failed_login_count` = 1. |
| 3 | Start new login with correct password. MFA challenge issued. | Partial auth state. |
| 4 | Send invalid TOTP (attempt 2). | HTTP 401; `failed_login_count` = 2. |
| 5 | Repeat login + invalid TOTP for attempts 3 and 4. | `failed_login_count` = 3, then 4. |
| 6 | Start new login with correct password. MFA challenge issued. | Partial auth state. |
| 7 | Send invalid TOTP (attempt 5 -- threshold). | HTTP 401 with lockout message; `failed_login_count` = 5; `locked_until` is set. |
| 8 | Verify `account_locked` audit event is logged. | Audit event present with `attempt_count = 5`. |
| 9 | Attempt login with correct password. | HTTP 401 with lockout message -- account is locked. |

## 6. Postconditions
- Account is locked purely from MFA failures (no password failures).
- `failed_login_count = 5`; `locked_until` is set.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
