---
id: TC-AUTH-002
user_story: US-AUTH-001
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-002: Login fails with wrong password

## 1. Test Objective
Verify that submitting an incorrect password returns a 401 Unauthorized response with a generic error message, increments the failed login counter, and does not reveal whether the email exists.

## 2. Related Requirements
- User Story: US-AUTH-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-9
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user record exists with email `john@acme.com` and an active membership in the "acme" tenant.
- The user's `failed_login_count` is 0 and account is not locked.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Email | john@acme.com | Valid registered email |
| Password | Wr0ngP@ssword! | Incorrect password |
| Expected HTTP Status | 401 | Unauthorized |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` to `acme.yourhrm.com` with `{ email: "john@acme.com", password: "Wr0ngP@ssword!" }` | Request is processed by the authentication endpoint. |
| 2 | Verify HTTP response status is 401 Unauthorized | Response status code is 401. |
| 3 | Verify response body contains a generic error message | Message is "Invalid email or password" -- does not reveal that the email exists. |
| 4 | Verify no JWT access token is returned in the response body | `accessToken` field is absent or null. |
| 5 | Verify no refresh token cookie is set | No `Set-Cookie` header for the refresh token. |
| 6 | Query the database and verify `failed_login_count` has been incremented to 1 | `users.failed_login_count = 1` for this user. |
| 7 | Verify a `login_failure` audit log entry is created | Audit record contains email, IP address, user_agent, and timestamp. |
| 8 | Verify the response does not leak timing information (response time is comparable to a successful login) | Response time is within acceptable variance of a successful login attempt. |

## 6. Postconditions
- The user's `failed_login_count` is incremented by 1.
- No tokens have been issued.
- A `login_failure` audit event is recorded.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
