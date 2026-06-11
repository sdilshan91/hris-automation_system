---
id: TC-AUTH-073
user_story: US-AUTH-009
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-073: Negative -- revoke non-existent session and non-admin calls admin endpoint

## 1. Test Objective
Verify that (a) attempting to revoke a session ID that does not exist returns an appropriate error, (b) a regular user (non-admin) calling the admin session management endpoints is denied with HTTP 403, and (c) attempting to revoke another user's session via the self-service endpoint is denied.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-5, AC-6
- Functional Requirements: FR-6, FR-8

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `admin@acme.com` has `Tenant Admin` role.
- User `jane@acme.com` has `Employee` role (no admin permissions).
- User `john@acme.com` has an active session S1.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin |
| Regular user | jane@acme.com | Employee role |
| Other user | john@acme.com | Has session S1 |
| Non-existent session ID | 00000000-0000-0000-0000-000000000000 | Does not exist |
| Admin sessions endpoint | GET /api/v1/tenant/users/{john-id}/sessions | Admin-only |
| Admin revoke endpoint | POST /api/v1/tenant/users/{john-id}/sessions/revoke | Admin-only |
| Self revoke endpoint | POST /api/v1/auth/me/sessions/{S1-id}/revoke | jane tries to revoke john's session |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As admin, call `POST /api/v1/tenant/users/{john-id}/sessions/revoke` with `{ "sessionId": "00000000-0000-0000-0000-000000000000" }`. | HTTP 404 Not Found; body indicates the session does not exist or does not belong to this user. |
| 2 | Verify no sessions were affected. | John's session S1 remains active. |
| 3 | As `jane@acme.com` (Employee), call `GET /api/v1/tenant/users/{john-id}/sessions`. | HTTP 403 Forbidden; jane does not have admin permissions. |
| 4 | As `jane@acme.com`, call `POST /api/v1/tenant/users/{john-id}/sessions/revoke` with `{ "sessionId": "<S1-id>" }`. | HTTP 403 Forbidden. |
| 5 | Verify john's session S1 is still active. | `revoked_at IS NULL` for S1. |
| 6 | As `jane@acme.com`, call `POST /api/v1/auth/me/sessions/{S1-id}/revoke` where S1 belongs to john. | HTTP 404 Not Found or HTTP 403 Forbidden; the system does not allow revoking another user's session via the self-service endpoint. |
| 7 | Verify no audit events were created for these failed attempts (or only failed-attempt audit logs). | No `session_revoked_by_admin` or `session_revoked_by_user` events. |
| 8 | As an unauthenticated user (no JWT), call `GET /api/v1/tenant/users/{john-id}/sessions`. | HTTP 401 Unauthorized. |
| 9 | As an unauthenticated user, call `GET /api/v1/auth/me/sessions`. | HTTP 401 Unauthorized. |

## 6. Postconditions
- No sessions were modified by any of the unauthorized or invalid attempts.
- Access control is enforced on all session management endpoints.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
