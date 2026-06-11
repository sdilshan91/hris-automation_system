---
id: TC-AUTH-070
user_story: US-AUTH-009
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-070: Admin revokes a specific session and revokes all sessions for a user

## 1. Test Objective
Verify that a tenant admin can (a) revoke a specific session by providing a `sessionId`, (b) revoke all sessions by omitting the body, (c) the action is logged in the audit trail, and (d) the affected user is forced to re-authenticate on their next refresh attempt. Also verify the notification requirement (BR-5).

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8, FR-9
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `admin@acme.com` has `Tenant Admin` role with an active session.
- User `john@acme.com` (Employee) has 3 active sessions (S1, S2, S3).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin |
| Target user | john@acme.com | userId = {john-id} |
| Revoke endpoint | POST /api/v1/tenant/users/{john-id}/sessions/revoke | Admin session revocation |
| Specific revoke body | `{ "sessionId": "<S2-id>" }` | Revoke Session 2 only |
| Revoke-all body | (empty or `{}`) | Revoke all remaining sessions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As admin, call `POST /api/v1/tenant/users/{john-id}/sessions/revoke` with `{ "sessionId": "<S2-id>" }`. | HTTP 200; response confirms Session S2 is revoked. |
| 2 | Verify Session S2 in the database. | `revoked_at` is set; S1 and S3 remain active (`revoked_at IS NULL`). |
| 3 | From S2's device, call `POST /api/v1/auth/refresh` with S2's refresh token. | HTTP 401 Unauthorized; S2's user is forced to re-authenticate. |
| 4 | From S1's device, call `POST /api/v1/auth/refresh` with S1's refresh token. | HTTP 200; S1 continues to work normally. |
| 5 | Verify `session_revoked_by_admin` audit event for specific revocation. | Audit record contains: `event_type = "session_revoked_by_admin"`, `admin_user_id`, `target_user_id`, `revoked_session_id = S2`, `tenant_id`. |
| 6 | Verify notification is sent to the affected user (BR-5). | If john has another active session, an in-app notification is delivered. If not, an email notification is sent. |
| 7 | As admin, call `POST /api/v1/tenant/users/{john-id}/sessions/revoke` with no body or empty body. | HTTP 200; response confirms all remaining sessions (S1, S3) are revoked. |
| 8 | Verify all of john's refresh tokens are now revoked. | All tokens have `revoked_at` set. |
| 9 | From S1's device, call `POST /api/v1/auth/refresh`. | HTTP 401 Unauthorized. |
| 10 | From S3's device, call `POST /api/v1/auth/refresh`. | HTTP 401 Unauthorized. |
| 11 | Verify `session_revoked_by_admin` audit events for revoke-all. | Audit records exist for each revoked session (S1 and S3) or a single bulk event referencing all revoked session IDs. |
| 12 | Verify email notification is sent (BR-5) since no active sessions remain. | Email notification is sent to john@acme.com about session termination. |

## 6. Postconditions
- All of john's sessions are revoked after the revoke-all action.
- John must re-authenticate from all devices.
- Audit log contains records of both the specific and bulk revocations.
- Notifications were delivered per BR-5.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
