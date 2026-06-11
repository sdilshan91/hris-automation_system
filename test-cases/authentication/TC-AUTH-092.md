---
id: TC-AUTH-092
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-092: Global lockout blocks login on all tenants (cross-tenant lockout enforcement)

## 1. Test Objective
Verify that account lockout is per global user account (BR-1): when a user is locked out via failed attempts on one tenant's login page, they cannot log in to ANY tenant they belong to. The lockout state is not tenant-scoped.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-1
- Functional Requirements: FR-1, FR-2, FR-5

## 3. Preconditions
- User `multi@shared.com` belongs to both tenant "acme" and tenant "globex."
- Both tenants have lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User has `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | multi@shared.com | Member of both tenants |
| Tenant A subdomain | acme | First tenant |
| Tenant B subdomain | globex | Second tenant |
| Wrong password | Wr0ngP@ss | Incorrect credential |
| Correct password | S3cure!Pass2026 | Valid credential |
| Max failed attempts | 5 | Both tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send 5 consecutive `POST /api/v1/auth/login` requests with wrong password via `acme.yourhrm.com` (or `X-Tenant-Subdomain: acme`). | 5th attempt returns lockout message; `locked_until` is set; `failed_login_count = 5`. |
| 2 | Verify `users.locked_until` is set (global user record, not tenant-specific). | Lockout is on the `users` table, not per-tenant. |
| 3 | Send `POST /api/v1/auth/login` with CORRECT password via `globex.yourhrm.com` (or `X-Tenant-Subdomain: globex`). | HTTP 401 with lockout message -- login is blocked on tenant B even though failures occurred on tenant A. |
| 4 | Verify no tokens are issued for the tenant B attempt. | No JWT or refresh token. |
| 5 | Send `POST /api/v1/auth/login` with CORRECT password via `acme.yourhrm.com`. | HTTP 401 with lockout message -- still blocked on the originating tenant too. |
| 6 | Wait for lockout to expire (or advance time). Log in with correct password via `globex.yourhrm.com`. | HTTP 200 OK; login succeeds on tenant B after lockout expiry. |
| 7 | Log in with correct password via `acme.yourhrm.com`. | HTTP 200 OK; login succeeds on tenant A after lockout expiry. |

## 6. Postconditions
- Global lockout prevented login on both tenants during the lockout period.
- After expiry, login is possible on both tenants.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
