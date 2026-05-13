---
id: TC-AUTH-008
user_story: US-AUTH-003
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-008: Logout invalidates tokens

## 1. Test Objective
Verify that when a user logs out, the refresh token is revoked, the httpOnly cookie is cleared, and subsequent refresh attempts fail with 401.

## 2. Related Requirements
- User Story: US-AUTH-003
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-7

## 3. Preconditions
- User `john@acme.com` is authenticated with a valid JWT and refresh token in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Authenticated user |
| Tenant | acme | Active tenant |
| Logout endpoint | POST /api/v1/auth/logout | No body required |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the user has an active session with a valid refresh token | `refresh_token` table shows a non-revoked record for this user-tenant pair. |
| 2 | Send `POST /api/v1/auth/logout` with the current JWT in the Authorization header | HTTP 200 OK with `{ message: "Logged out successfully" }`. |
| 3 | Verify the `Set-Cookie` header clears the refresh token cookie | Cookie is set to an expired value with the same domain, path, and security attributes (`httpOnly; Secure; SameSite=Strict`). |
| 4 | Query the `refresh_token` table for the user's token | `revoked_at` is set to the current timestamp. |
| 5 | Verify a `logout` audit log entry is created | Audit record contains `event_type: "logout"`, user_id, tenant_id, ip_address, user_agent, timestamp. |
| 6 | Attempt to use the previously issued access token for an API request | Request succeeds (stateless JWT remains valid until its ~15 min expiry). |
| 7 | Verify the Angular SPA clears the in-memory access token and navigates to the login page | Frontend state is cleared; user sees the tenant-branded login page. |

## 6. Postconditions
- The refresh token is revoked in the database.
- The refresh token cookie is cleared in the browser.
- The access token is technically valid until natural expiry but the client has cleared it.
- A `logout` audit event is recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
