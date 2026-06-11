---
id: TC-AUTH-071
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-071: User views own sessions and revokes a non-current session

## 1. Test Objective
Verify that a user can view all of their active sessions in the current tenant via `GET /api/v1/auth/me/sessions`, that the response includes correct device/browser/IP/timestamp data with a `isCurrent` flag on the current session, and that the user can revoke any non-current session via `POST /api/v1/auth/me/sessions/{sessionId}/revoke`.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-6
- Functional Requirements: FR-7, FR-8, FR-9
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `jane@acme.com` has 3 active sessions:
  - S1: Chrome/Windows (the current session for this test)
  - S2: Safari/macOS
  - S3: Firefox/Linux

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | jane@acme.com | Employee role |
| Current session | S1 (Chrome/Windows) | Used for API calls in this test |
| Non-current session | S2 (Safari/macOS) | Target for revocation |
| Self sessions endpoint | GET /api/v1/auth/me/sessions | Self-service |
| Self revoke endpoint | POST /api/v1/auth/me/sessions/{S2-id}/revoke | Revoke non-current |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | From S1, call `GET /api/v1/auth/me/sessions`. | HTTP 200 is returned. |
| 2 | Inspect the response array. | Exactly 3 session objects are returned. |
| 3 | Verify each session object structure. | Each contains: `sessionId`, `device`, `browser`, `os`, `ipAddress`, `issuedAt`, `lastActiveAt`, `isCurrent`. |
| 4 | Verify S1 is marked as current. | S1 has `isCurrent: true`. |
| 5 | Verify S2 and S3 are marked as non-current. | S2 and S3 each have `isCurrent: false`. |
| 6 | From S1, call `POST /api/v1/auth/me/sessions/{S2-id}/revoke`. | HTTP 200; response confirms S2 is revoked. |
| 7 | Verify S2 is revoked in the database. | S2's `revoked_at` is set. |
| 8 | From S2's device, call `POST /api/v1/auth/refresh` with S2's token. | HTTP 401 Unauthorized. |
| 9 | Verify S1 and S3 remain active. | Both have `revoked_at IS NULL`. |
| 10 | Call `GET /api/v1/auth/me/sessions` again from S1. | HTTP 200; only S1 and S3 are listed. S2 no longer appears. |
| 11 | Verify `session_revoked_by_user` audit event is logged. | Audit record contains: `event_type = "session_revoked_by_user"`, `user_id`, `revoked_session_id = S2`, `tenant_id`. |

## 6. Postconditions
- S2 is revoked; S1 and S3 remain active.
- The user on S2's device must re-authenticate.
- Audit log records the self-service revocation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
