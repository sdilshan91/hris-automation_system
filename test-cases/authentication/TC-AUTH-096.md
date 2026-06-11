---
id: TC-AUTH-096
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-096: Social login failures do NOT increment the lockout counter

## 1. Test Objective
Verify that social login failures (e.g., IdP returns an error) do NOT increment the `failed_login_count` counter (BR-6). Only local credential (password) and MFA failures count toward lockout.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-6

## 3. Preconditions
- User `alice@acme.com` has linked a social login provider (e.g., Google) and has `failed_login_count = 0`.
- The social login flow is functional.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Social login enabled |
| Social provider | Google | Example IdP |
| failed_login_count | 0 | Initial state |
| Max failed attempts | 5 | Tenant policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Initiate a social login (Google) that returns an IdP error (e.g., invalid state, token exchange failure). | Login fails; user sees an appropriate error message. |
| 2 | Query `users.failed_login_count` for `alice@acme.com`. | Value remains `0` -- social login failure did NOT increment the counter. |
| 3 | Repeat the social login failure 5 more times (total 6 social failures). | Each attempt fails. |
| 4 | Query `users.failed_login_count`. | Still `0` -- no social failures counted. |
| 5 | Verify `users.locked_until` is still `null`. | Account is NOT locked despite multiple social login failures. |
| 6 | Send `POST /api/v1/auth/login` with wrong password. | HTTP 401; `failed_login_count` becomes `1`. Only local credential failure increments. |
| 7 | Send another social login that fails. | Login fails. |
| 8 | Query `users.failed_login_count`. | Value remains `1` -- the social failure between local failures does not affect the count. |

## 6. Postconditions
- `failed_login_count` reflects only local credential and MFA failures.
- Social login failures are excluded from the lockout counter.
- Account is not locked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
