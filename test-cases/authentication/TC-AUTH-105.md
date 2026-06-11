---
id: TC-AUTH-105
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-105: Failed login attempts accumulate across tenants toward global lockout

## 1. Test Objective
Verify that because lockout is per global user account (BR-1), failed login attempts on different tenants accumulate toward the same threshold. A user who fails 3 times on tenant A and 2 times on tenant B should be locked out (total = 5 = threshold).

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- User `multi@shared.com` belongs to tenants "acme" and "globex."
- Both tenants have `maxFailedAttempts = 5`.
- `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | multi@shared.com | Multi-tenant user |
| Tenant A | acme | First tenant |
| Tenant B | globex | Second tenant |
| Wrong password | Wr0ngP@ss | Incorrect |
| Max failed attempts | 5 | Combined threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with wrong password via `acme.yourhrm.com` (attempt 1). | HTTP 401; `failed_login_count = 1`. |
| 2 | Send login with wrong password via `acme.yourhrm.com` (attempt 2). | HTTP 401; `failed_login_count = 2`. |
| 3 | Send login with wrong password via `globex.yourhrm.com` (attempt 3). | HTTP 401; `failed_login_count = 3`. Failures accumulate across tenants. |
| 4 | Send login with wrong password via `acme.yourhrm.com` (attempt 4). | HTTP 401; `failed_login_count = 4`. |
| 5 | Send login with wrong password via `globex.yourhrm.com` (attempt 5 -- threshold). | HTTP 401 with lockout message; `failed_login_count = 5`; `locked_until` is set. |
| 6 | Verify the account is locked globally (see TC-AUTH-092). | Login blocked on both tenants. |

## 6. Postconditions
- Cross-tenant failure accumulation triggered global lockout at the threshold.
- A single `failed_login_count` counter is shared across all tenant login pages.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
