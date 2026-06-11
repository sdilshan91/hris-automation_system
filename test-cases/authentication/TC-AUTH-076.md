---
id: TC-AUTH-076
user_story: US-AUTH-009
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-076: Session metadata is not exposed to other users (NFR-5)

## 1. Test Objective
Verify that session metadata (user_agent, IP address, device details) is only visible to (a) the session owner via the self-service endpoint and (b) tenant admins via the admin endpoint. Other users in the same tenant cannot see another user's session metadata.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-4, AC-6
- Functional Requirements: FR-6, FR-7
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `john@acme.com` (Employee role) has 2 active sessions.
- User `jane@acme.com` (Employee role, same tenant, non-admin) has 1 active session.
- User `admin@acme.com` (Tenant Admin role) has 1 active session.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User A | john@acme.com | Employee, has 2 sessions |
| User B | jane@acme.com | Employee, same tenant |
| Admin | admin@acme.com | Tenant Admin |
| Self endpoint | GET /api/v1/auth/me/sessions | User's own sessions |
| Admin endpoint | GET /api/v1/tenant/users/{id}/sessions | Admin view |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `john@acme.com`, call `GET /api/v1/auth/me/sessions`. | HTTP 200; john sees his own 2 sessions with full metadata (device, browser, OS, IP, timestamps). |
| 2 | As `jane@acme.com`, call `GET /api/v1/auth/me/sessions`. | HTTP 200; jane sees only her own 1 session. John's sessions are NOT included. |
| 3 | As `jane@acme.com`, call `GET /api/v1/tenant/users/{john-id}/sessions`. | HTTP 403 Forbidden; jane does not have admin permissions to view john's sessions. |
| 4 | As `admin@acme.com`, call `GET /api/v1/tenant/users/{john-id}/sessions`. | HTTP 200; admin sees john's 2 sessions with full metadata. |
| 5 | As `admin@acme.com`, call `GET /api/v1/tenant/users/{jane-id}/sessions`. | HTTP 200; admin sees jane's 1 session with full metadata. |
| 6 | Verify no session endpoint returns sessions belonging to a different user when called by a non-admin. | The self-service endpoint always scopes results to the authenticated user. |
| 7 | Verify IP addresses and user-agent strings are not included in any non-session API response (e.g., user profile, user list). | Session metadata only appears in dedicated session endpoints. |
| 8 | As `john@acme.com`, attempt to guess another user's session ID and call `POST /api/v1/auth/me/sessions/{jane-session-id}/revoke`. | HTTP 404; the session does not belong to john. |

## 6. Postconditions
- No session metadata was exposed to unauthorized users.
- Admin access to session data is verified.
- Self-service endpoint only returns the authenticated user's sessions.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
