---
id: TC-AUTH-072
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-072: User cannot revoke their own current session (BR-4)

## 1. Test Objective
Verify that the self-service session revocation endpoint rejects attempts to revoke the user's current session, preventing accidental self-lockout per BR-4. The user must use the logout function instead.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `jane@acme.com` has 2 active sessions:
  - S1: Chrome/Windows (the current session)
  - S2: Safari/macOS

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | jane@acme.com | Employee role |
| Current session | S1 | Used for this API call |
| Self revoke endpoint | POST /api/v1/auth/me/sessions/{S1-id}/revoke | Attempt to revoke current session |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | From S1, call `GET /api/v1/auth/me/sessions`. | HTTP 200; S1 appears with `isCurrent: true`. |
| 2 | Note the `sessionId` of S1 (the current session). | S1's session ID is captured. |
| 3 | From S1, call `POST /api/v1/auth/me/sessions/{S1-id}/revoke`. | HTTP 400 Bad Request or HTTP 409 Conflict. |
| 4 | Inspect the error response. | Body contains a message such as "Cannot revoke your current session. Use the logout function instead." |
| 5 | Verify S1 is still active in the database. | `revoked_at IS NULL` for S1. |
| 6 | Verify S2 is also still active. | No side effects on other sessions. |
| 7 | Verify no audit event is logged for this failed attempt. | No `session_revoked_by_user` event for S1. |
| 8 | Call `POST /api/v1/auth/logout` from S1. | HTTP 200; S1 is properly terminated via the logout function. |
| 9 | Verify S1 is now revoked. | `revoked_at` is set for S1 after logout. |

## 6. Postconditions
- Current session revocation via self-service is blocked.
- The user can still terminate their current session via the logout endpoint.
- No unintended session modifications occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
