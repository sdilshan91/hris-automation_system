---
id: TC-AUTH-009
user_story: US-AUTH-003
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-009: Refresh token cannot be reused after logout

## 1. Test Objective
Verify that after logout, the revoked refresh token cannot be used to obtain new tokens, and that reuse detection triggers chain revocation.

## 2. Related Requirements
- User Story: US-AUTH-003
- Acceptance Criteria: AC-1, AC-2
- User Story: US-AUTH-002
- Acceptance Criteria: AC-3 (reuse detection)
- Functional Requirements: FR-2, FR-7 (US-AUTH-002)

## 3. Preconditions
- User `john@acme.com` is authenticated in tenant "acme".
- The user's refresh token value has been captured before logout.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Authenticated user |
| Tenant | acme | Active tenant |
| Captured refresh token | (Token A - pre-logout) | To be reused after logout |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate and capture the refresh token (Token A) | Token A is stored for later reuse testing. |
| 2 | Send `POST /api/v1/auth/logout` | HTTP 200; Token A is revoked in the database. |
| 3 | Send `POST /api/v1/auth/refresh` with the captured Token A cookie | HTTP 401 Unauthorized; refresh is denied. |
| 4 | Verify the response message indicates the token is invalid or session has ended | Response contains appropriate error message. |
| 5 | Verify the system detects the reuse of a revoked token | The reuse detection logic identifies Token A as revoked. |
| 6 | If the user had other active sessions (tokens in the same chain), verify the entire token chain is revoked | All related refresh tokens for this session chain are revoked. |
| 7 | Verify a security event is logged | Audit record indicates potential token reuse/theft attempt. |
| 8 | Attempt to access any protected API endpoint without valid tokens | HTTP 401 Unauthorized; user must re-authenticate. |

## 6. Postconditions
- All tokens in the session chain are revoked.
- The user must perform a fresh login to obtain new tokens.
- A security audit event is recorded.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
