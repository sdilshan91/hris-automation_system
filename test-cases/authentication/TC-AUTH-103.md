---
id: TC-AUTH-103
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-103: Custom tenant lockout policy is applied (non-default maxFailedAttempts and duration)

## 1. Test Objective
Verify that the lockout mechanism respects the tenant's custom lockout policy. If a tenant configures `maxFailedAttempts = 3` and `lockoutDurationMinutes = 30`, the lockout triggers on the 3rd failure and lasts 30 minutes instead of the defaults.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-3
- Business Rules: BR-5

## 3. Preconditions
- Tenant "beta" has custom lockout policy: `maxFailedAttempts = 3`, `lockoutDurationMinutes = 30`.
- User `carol@beta.com` has `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | beta | Custom policy |
| User email | carol@beta.com | Active user |
| Wrong password | Wr0ngP@ss | Incorrect |
| Custom max attempts | 3 | Non-default |
| Custom lockout duration | 30 minutes | Non-default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify tenant "beta" has `maxFailedAttempts = 3`, `lockoutDurationMinutes = 30`. | Policy confirmed. |
| 2 | Send `POST /api/v1/auth/login` with wrong password (attempt 1). | HTTP 401 with generic message; `failed_login_count = 1`. |
| 3 | Send `POST /api/v1/auth/login` with wrong password (attempt 2). | HTTP 401 with generic message; `failed_login_count = 2`. |
| 4 | Send `POST /api/v1/auth/login` with wrong password (attempt 3 -- the custom threshold). | HTTP 401 with lockout message; `failed_login_count = 3`. |
| 5 | Query `users.locked_until`. | Value is approximately `now() + 30 minutes` (the custom duration, not the default 15). |
| 6 | Verify the account was NOT locked on attempts 1-2 (only on the 3rd). | Lockout triggered at the custom threshold, not the default 5. |

## 6. Postconditions
- Account is locked for 30 minutes after 3 failures.
- Tenant-specific policy overrides the system defaults.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
