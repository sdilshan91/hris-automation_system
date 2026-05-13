---
id: TC-AUTH-007
user_story: US-AUTH-002
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-007: Expired access token triggers refresh

## 1. Test Objective
Verify that when a JWT access token expires, the Angular HTTP interceptor silently refreshes the session using the refresh token, and the user experience is seamless.

## 2. Related Requirements
- User Story: US-AUTH-002
- Acceptance Criteria: AC-2, AC-4
- Functional Requirements: FR-3, FR-8, FR-10
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- User is authenticated with a valid refresh token and an access token that is expired or about to expire.
- The refresh token is valid (not expired, not revoked).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Authenticated user |
| Tenant | acme | Active tenant |
| Access token status | Expired | Past `exp` claim |
| Refresh token status | Valid | Within 7-day window |
| Refresh endpoint | POST /api/v1/auth/refresh | Cookie-based |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate and obtain a JWT access token and refresh token | Tokens are issued successfully. |
| 2 | Wait for the access token to expire (or set test expiry to a short duration, e.g., 30 seconds) | Access token `exp` claim is in the past. |
| 3 | Make an authenticated API request (e.g., `GET /api/v1/tenant/dashboard`) with the expired access token | API returns 401 Unauthorized due to expired token. |
| 4 | Observe the Angular HTTP interceptor behavior | Interceptor catches the 401, sends `POST /api/v1/auth/refresh` with the httpOnly cookie. |
| 5 | Verify the refresh endpoint returns HTTP 200 with a new access token | New JWT is issued with fresh `iat` and `exp`. |
| 6 | Verify the interceptor replays the original failed request with the new access token | The dashboard API call succeeds with the new token. |
| 7 | Verify the user does not see any interruption | No login redirect, no error message, no visible refresh indicator. |
| 8 | Test with an expired refresh token: wait for refresh token expiry (or use a token past `expires_at`) | Refresh returns 401 Unauthorized. |
| 9 | Verify the frontend redirects to the login page with a "Session expired" toast notification | User is directed to re-authenticate. |
| 10 | Test concurrent 401s: fire multiple API requests simultaneously with an expired access token | Only one refresh request is sent; other requests are queued and replayed after the refresh completes. |

## 6. Postconditions
- On successful refresh: user has a new valid access token and continues working seamlessly.
- On failed refresh (expired refresh token): user is redirected to the login page.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
