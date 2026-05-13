---
id: TC-AUTH-001
user_story: US-AUTH-001
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-001: Successful login with valid credentials

## 1. Test Objective
Verify that a tenant user with valid credentials and an active membership in the resolved tenant can successfully log in and receive a JWT access token and refresh token.

## 2. Related Requirements
- User Story: US-AUTH-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-5, FR-6, FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com` is provisioned.
- A user record exists with email `john@acme.com` and a valid `password_hash` (BCrypt).
- The user has an active `user_tenant` membership (status = `active`) for the "acme" tenant.
- The user's `is_active` flag is `true` and account is not locked (`locked_until` is null).
- MFA is not enabled for this user.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Email | john@acme.com | Valid registered email |
| Password | S3cure!Pass2026 | Correct password |
| Expected Role | Employee | Assigned role in this tenant |
| MFA Enabled | false | No MFA configured |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/login` | Tenant-branded login page is displayed with logo and primary color from tenant profile. |
| 2 | Enter email `john@acme.com` in the email field | Email field accepts the input. |
| 3 | Enter password `S3cure!Pass2026` in the password field | Password field masks the input. |
| 4 | Click the "Log in" button | Loading spinner appears on the button; button is disabled to prevent double-submit. |
| 5 | Observe the API call `POST /api/v1/auth/login` with body `{ email: "john@acme.com", password: "S3cure!Pass2026" }` | Request is sent over HTTPS to the correct endpoint. |
| 6 | Verify HTTP response status is 200 OK | Response status code is 200. |
| 7 | Verify response body contains `{ accessToken, user, tenant, permissions }` | All fields are present and populated. |
| 8 | Decode the JWT access token and verify claims | Token contains `sub`, `email`, `tenant_id` (matching acme tenant), `user_tenant_id`, `roles: ["Employee"]`, `permissions[]`, `iat`, `exp`; signed with RS256; expiry is approximately 15 minutes from `iat`. |
| 9 | Verify the `Set-Cookie` response header contains the refresh token | Cookie is `httpOnly; Secure; SameSite=Strict` with approximately 7-day expiry. |
| 10 | Verify user is redirected to the tenant dashboard | Browser navigates to `https://acme.yourhrm.com/dashboard`. |
| 11 | Verify a `login_success` audit log entry is created | Audit record contains user_id, tenant_id, IP address, user_agent, and timestamp. |

## 6. Postconditions
- User is authenticated and has access to the tenant dashboard.
- A valid refresh token record exists in the `refresh_token` table for this user-tenant pair.
- An audit log entry of type `login_success` has been recorded.
- `failed_login_count` for the user is 0.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
