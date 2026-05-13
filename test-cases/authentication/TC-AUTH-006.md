---
id: TC-AUTH-006
user_story: US-AUTH-002
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-006: Refresh token rotation works

## 1. Test Objective
Verify that when a refresh token is used, the system issues a new access token and a new rotated refresh token, while revoking the old refresh token and linking them via `replaced_by_token_id`.

## 2. Related Requirements
- User Story: US-AUTH-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-6, FR-8, FR-9

## 3. Preconditions
- User `john@acme.com` is authenticated with a valid JWT and refresh token in tenant "acme".
- The current refresh token (Token A) is stored in the `refresh_token` table.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Authenticated user |
| Tenant | acme | Active tenant |
| Token A | (current refresh token) | Valid, non-revoked |
| Refresh endpoint | POST /api/v1/auth/refresh | Cookie-based |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record the current refresh token hash (Token A) from the `refresh_token` table | Token A exists with `revoked_at = null`, `replaced_by_token_id = null`. |
| 2 | Send `POST /api/v1/auth/refresh` with the httpOnly cookie containing Token A | HTTP 200 response. |
| 3 | Verify a new access token is returned in the response body | New JWT with valid claims and fresh `iat`/`exp`. |
| 4 | Verify a new refresh token cookie (Token B) is set via `Set-Cookie` header | New cookie with `httpOnly; Secure; SameSite=Strict` attributes. |
| 5 | Query the `refresh_token` table for Token A | Token A now has `revoked_at` set to a timestamp and `replaced_by_token_id` pointing to Token B's record. |
| 6 | Query the `refresh_token` table for Token B | Token B exists with `revoked_at = null`, fresh `issued_at` and `expires_at` (~7 days), and `user_agent` and `ip_address` recorded. |
| 7 | Verify Token B is a different value than Token A | The two tokens are distinct cryptographically random values. |
| 8 | Use Token B to perform another refresh | HTTP 200; Token B is revoked, Token C is issued -- rotation chain continues correctly. |

## 6. Postconditions
- Token A is revoked in the database with `replaced_by_token_id` pointing to Token B.
- Token B is the active refresh token for this session.
- A new valid access token has been issued.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
