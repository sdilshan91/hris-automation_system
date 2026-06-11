---
id: TC-AUTH-069
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-069: Admin views a user's active sessions with device, browser, IP, and timestamps

## 1. Test Objective
Verify that a tenant admin can retrieve the list of active sessions for any user in their tenant via `GET /api/v1/tenant/users/{id}/sessions`, and that the response contains correctly parsed device/browser/OS information, IP address, `issued_at`, `last_active_at`, and the `isCurrent` flag.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `admin@acme.com` has `Tenant Admin` role.
- User `john@acme.com` (Employee role) has 3 active sessions from different devices/browsers.
- Each session has a distinct `user_agent` and `ip_address` in the `refresh_token` table.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Target user | john@acme.com | Employee, userId = {john-id} |
| Session 1 UA | Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0 | Chrome on Windows |
| Session 1 IP | 192.168.1.10 | Office network |
| Session 2 UA | Mozilla/5.0 (Macintosh; Intel Mac OS X) Safari/17.5 | Safari on macOS |
| Session 2 IP | 10.0.0.42 | Home network |
| Session 3 UA | Mozilla/5.0 (Linux; Android 14) Mobile | Mobile browser |
| Session 3 IP | 203.0.113.5 | Mobile network |
| Endpoint | GET /api/v1/tenant/users/{john-id}/sessions | Admin endpoint |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` with Tenant Admin JWT. | Admin session established. |
| 2 | Call `GET /api/v1/tenant/users/{john-id}/sessions` with admin JWT. | HTTP 200 is returned. |
| 3 | Inspect the response array. | Exactly 3 session objects are returned (only active, non-revoked, non-expired). |
| 4 | Verify each session object structure. | Each contains: `sessionId` (UUID), `device`, `browser`, `os`, `ipAddress`, `issuedAt` (ISO 8601), `lastActiveAt` (ISO 8601), `isCurrent` (boolean). |
| 5 | Verify Session 1 parsed fields. | `browser = "Chrome"`, `os = "Windows"`, `device = "Desktop"`, `ipAddress = "192.168.1.10"`. |
| 6 | Verify Session 2 parsed fields. | `browser = "Safari"`, `os = "macOS"`, `device = "Desktop"`, `ipAddress = "10.0.0.42"`. |
| 7 | Verify Session 3 parsed fields. | `browser` is detected as the mobile browser, `os = "Android"`, `device = "Mobile"`, `ipAddress = "203.0.113.5"`. |
| 8 | Verify `isCurrent` flag. | All 3 sessions show `isCurrent: false` (the admin is viewing another user's sessions). |
| 9 | Verify sessions are ordered by `issuedAt` (most recent first) or as specified by the API. | Sessions are returned in a consistent, documented order. |
| 10 | Verify revoked and expired sessions are excluded. | If the user has any revoked or expired tokens in the DB, they do not appear in the response. |

## 6. Postconditions
- No sessions were modified by the read operation.
- Admin can see all session metadata for the target user.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
