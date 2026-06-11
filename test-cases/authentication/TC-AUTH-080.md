---
id: TC-AUTH-080
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-080: Session policy configuration via PUT /api/v1/tenant/auth-settings

## 1. Test Objective
Verify that a tenant admin can configure session policy settings (`idleTimeoutMinutes`, `absoluteTimeoutHours`, `maxConcurrentSessions`, `concurrentSessionStrategy`) via `PUT /api/v1/tenant/auth-settings` and that the new settings take effect for subsequent logins and token refreshes. Also verify input validation and default values (FR-1).

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1, AC-2, AC-3
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" is in `active` state.
- User `admin@acme.com` has `Tenant Admin` role.
- Current session policy is at defaults (idleTimeoutMinutes=60, absoluteTimeoutHours=24, maxConcurrentSessions=5, concurrentSessionStrategy="deny_new").

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin | admin@acme.com | Tenant Admin |
| Endpoint | PUT /api/v1/tenant/auth-settings | Session policy update |
| Valid update | `{ "idleTimeoutMinutes": 30, "absoluteTimeoutHours": 12, "maxConcurrentSessions": 3, "concurrentSessionStrategy": "revoke_oldest" }` | All fields |
| Partial update | `{ "idleTimeoutMinutes": 15 }` | Only one field |
| Invalid strategy | `{ "concurrentSessionStrategy": "invalid_value" }` | Should be rejected |
| Zero timeout | `{ "idleTimeoutMinutes": 0 }` | Boundary -- should be rejected |
| Negative value | `{ "maxConcurrentSessions": -1 }` | Should be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As admin, call `GET /api/v1/tenant/auth-settings`. | HTTP 200; returns current settings with default values. |
| 2 | Call `PUT /api/v1/tenant/auth-settings` with the valid update body. | HTTP 200; all four fields are updated. |
| 3 | Call `GET /api/v1/tenant/auth-settings` again. | Reflects the new values: idleTimeoutMinutes=30, absoluteTimeoutHours=12, maxConcurrentSessions=3, concurrentSessionStrategy="revoke_oldest". |
| 4 | Verify the new policy takes effect: log in a user who already has 3 sessions. | The revoke_oldest strategy applies to the new login attempt. |
| 5 | Call `PUT /api/v1/tenant/auth-settings` with `{ "concurrentSessionStrategy": "invalid_value" }`. | HTTP 400 or 422; validation error indicating invalid strategy value. Only "deny_new" and "revoke_oldest" are accepted. |
| 6 | Call `PUT /api/v1/tenant/auth-settings` with `{ "idleTimeoutMinutes": 0 }`. | HTTP 400 or 422; idle timeout must be a positive integer. |
| 7 | Call `PUT /api/v1/tenant/auth-settings` with `{ "maxConcurrentSessions": -1 }`. | HTTP 400 or 422; max concurrent sessions must be a positive integer. |
| 8 | Call `PUT /api/v1/tenant/auth-settings` with `{ "absoluteTimeoutHours": 0 }`. | HTTP 400 or 422; absolute timeout must be a positive integer. |
| 9 | As a non-admin user, call `PUT /api/v1/tenant/auth-settings`. | HTTP 403 Forbidden. |
| 10 | Verify that updating session policy does NOT retroactively terminate existing sessions. | All currently active sessions remain active; new policies apply to new logins and future refresh attempts. |

## 6. Postconditions
- Session policy settings are persisted and take effect.
- Invalid inputs are rejected with clear validation messages.
- Only admins can modify session policies.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
