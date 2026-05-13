---
id: TC-AUTH-003
user_story: US-AUTH-001
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-003: Login fails with non-existent username

## 1. Test Objective
Verify that submitting a non-existent email address returns the same generic 401 response as an incorrect password, preventing user enumeration.

## 2. Related Requirements
- User Story: US-AUTH-001
- Acceptance Criteria: AC-2
- Non-Functional Requirements: NFR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No user record exists with email `nonexistent@acme.com`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Email | nonexistent@acme.com | Email not registered in system |
| Password | AnyP@ssword1 | Any password value |
| Expected HTTP Status | 401 | Unauthorized |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` to `acme.yourhrm.com` with `{ email: "nonexistent@acme.com", password: "AnyP@ssword1" }` | Request is processed by the authentication endpoint. |
| 2 | Verify HTTP response status is 401 Unauthorized | Response status code is 401. |
| 3 | Verify response body contains the same generic error message as TC-AUTH-002 | Message is "Invalid email or password" -- identical to wrong password response. |
| 4 | Verify no JWT access token or refresh token is returned | No tokens issued. |
| 5 | Measure response time and compare with TC-AUTH-002 response time | Response times are comparable (within 100ms variance) to prevent timing-based user enumeration. |
| 6 | Verify the response headers do not leak user existence information | No headers or body fields indicate whether the email is registered. |

## 6. Postconditions
- No tokens have been issued.
- No `failed_login_count` is incremented (user does not exist).
- A `login_failure` audit event may be recorded with the attempted email.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
