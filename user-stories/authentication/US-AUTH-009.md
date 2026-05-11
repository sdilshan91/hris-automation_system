---
id: US-AUTH-009
module: Authentication & Authorization
priority: Should Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-009: Session management and concurrent session limits

## 1. Description
**As a** tenant admin,
**I want to** configure session policies (idle timeout, absolute timeout, concurrent session limits) and view/manage active sessions for users in my tenant,
**So that** I can enforce security policies and respond to suspicious activity by terminating sessions.

## 2. Preconditions
- The tenant admin is authenticated with `Tenant Admin` or `Tenant Owner` role.
- The tenant is in `active` or `trial` state.
- The `refresh_token` table records active sessions with metadata (user_agent, ip_address, issued_at).
- Session policy settings are configurable in the tenant's auth settings.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant admin configures session policy with `maxConcurrentSessions = 3` | A user already has 3 active sessions and authenticates from a 4th device | The system either (a) denies the new login with a message "Maximum concurrent sessions reached" or (b) automatically revokes the oldest session and creates the new one (configurable strategy). |
| AC-2 | A tenant admin configures `idleTimeout = 30 minutes` | A user's last activity was > 30 minutes ago and they attempt to refresh their token | The system checks the `last_active_at` timestamp, determines the session is idle-expired, revokes the refresh token, and returns 401, forcing re-authentication. |
| AC-3 | A tenant admin configures `absoluteTimeout = 12 hours` | A user has been continuously active for > 12 hours | The system revokes the refresh token regardless of activity, returns 401 on the next refresh attempt, and requires re-authentication. |
| AC-4 | A tenant admin navigates to a user's active sessions page | They view the session list | The system displays all active (non-revoked, non-expired) refresh tokens for that user in this tenant, showing: device/browser (parsed from user_agent), IP address, issued_at, and last_active_at. |
| AC-5 | A tenant admin revokes a specific session for a user | They call `POST /api/v1/tenant/users/{id}/sessions/revoke` with `{ sessionId }` or without body (revoke all) | The system revokes the specified refresh token(s), logs the action in the tenant audit log, and the affected user is forced to re-authenticate on their next refresh attempt. |
| AC-6 | A user views their own active sessions from their profile | They access `GET /api/v1/auth/me/sessions` | The system returns all their active sessions in the current tenant with device info, IP, and timestamps, and allows them to revoke any session except the current one. |

## 4. Functional Requirements
- FR-1: Session policy SHALL be configurable per tenant via `PUT /api/v1/tenant/auth-settings` with fields: `idleTimeoutMinutes` (default: 60), `absoluteTimeoutHours` (default: 24), `maxConcurrentSessions` (default: 5), `concurrentSessionStrategy` ("deny_new" | "revoke_oldest").
- FR-2: The refresh endpoint SHALL check idle timeout by comparing the current time against the session's `last_active_at` timestamp.
- FR-3: The refresh endpoint SHALL check absolute timeout by comparing the current time against the session's `issued_at` timestamp.
- FR-4: On each successful API request, the system SHALL update `last_active_at` for the user's current session (debounced to avoid excessive writes, e.g., at most once per minute).
- FR-5: The concurrent session check SHALL occur at login time and count non-revoked, non-expired refresh tokens for the user-tenant pair.
- FR-6: Active sessions endpoint for admins: `GET /api/v1/tenant/users/{id}/sessions`.
- FR-7: Active sessions endpoint for self: `GET /api/v1/auth/me/sessions`.
- FR-8: Session revocation endpoint: `POST /api/v1/tenant/users/{id}/sessions/revoke` (admin) and `POST /api/v1/auth/me/sessions/{sessionId}/revoke` (self).
- FR-9: All session management actions SHALL be recorded in the tenant audit log.
- FR-10: A Hangfire background job SHALL periodically clean up expired and revoked refresh tokens older than a configurable retention period (default: 30 days).

## 5. Non-Functional Requirements
- NFR-1: Session activity tracking (`last_active_at` update) SHALL add <= 2 ms overhead per request (debounced, async write).
- NFR-2: Concurrent session counting SHALL be performant with an index on `(user_id, tenant_id, revoked_at)` in the `refresh_token` table.
- NFR-3: Session list queries SHALL return within 200 ms at P95.
- NFR-4: The system SHALL handle clock drift between API instances gracefully (NTP synchronized, <= 1 second drift).
- NFR-5: Session metadata (user_agent, IP) SHALL be stored but never exposed to other users (only to the session owner and tenant admins).

## 6. Business Rules
- BR-1: Session policies are per-tenant; different tenants can have different timeout and concurrency limits.
- BR-2: System admin sessions on `admin.yourhrm.com` follow system-level session policies, not individual tenant policies.
- BR-3: Impersonation sessions do not count toward the impersonated user's concurrent session limit.
- BR-4: The "current session" cannot be revoked by the user through the self-service UI (to prevent accidental self-lockout); they must use the logout function instead.
- BR-5: When a session is revoked by an admin, the affected user receives an in-app notification (if they have another active session) or email notification.
- BR-6: Idle timeout is reset by any authenticated API request (not just token refresh).

## 7. Data Requirements
- **`refresh_token` table (extended):** add `last_active_at` (timestamptz) column to track session activity.
- **Tenant auth settings (in tenant configuration):** `idle_timeout_minutes` (int), `absolute_timeout_hours` (int), `max_concurrent_sessions` (int), `concurrent_session_strategy` (enum).
- **Session list response:** `[{ sessionId, device, browser, os, ipAddress, issuedAt, lastActiveAt, isCurrent }]` (device/browser/os parsed from user_agent).
- **Audit records:** `session_revoked_by_admin`, `session_revoked_by_user`, `session_expired_idle`, `session_expired_absolute`, `concurrent_session_denied`, `concurrent_session_oldest_revoked`.

## 8. UI/UX Notes
- Notion-like design for the session management page:
  - User's "Active Sessions" in profile settings: a list showing each session as a card with device icon, browser name, IP, location (optional, derived from IP), "Current session" badge, and a "Revoke" button.
  - Tenant admin view: under User Management, clicking a user shows their sessions tab with the same cards plus admin revoke actions.
- Session policy settings in Tenant Admin > Security Settings: clean form fields for timeout values and concurrent session limit with a dropdown for the strategy.
- When a session is denied due to concurrent limit: "You have reached the maximum number of active sessions. Please log out from another device or contact your administrator."
- Idle timeout warning: 5 minutes before idle expiry, display an in-app modal "Your session is about to expire due to inactivity. Click to stay logged in." with a countdown timer.
- Mobile: session cards stack vertically; idle timeout warning appears as a bottom sheet.

## 9. Dependencies
- US-AUTH-001 (Login) for session creation at login.
- US-AUTH-002 (JWT/Refresh) for refresh token management and revocation.
- US-AUTH-003 (Logout) for session termination.
- Redis for optional session activity caching (debounced `last_active_at` updates).
- Hangfire for expired session cleanup background job.

## 10. Assumptions & Constraints
- Session management is based on the `refresh_token` table; each non-revoked, non-expired refresh token represents an active session.
- Idle timeout cannot be enforced at the exact second due to debounced activity tracking; it is approximate within the debounce interval (e.g., +/- 1 minute).
- The frontend is responsible for the idle timeout warning UI and proactive session keep-alive pings.
- IP-based geolocation for session display is a nice-to-have, not a hard requirement for Phase 1.

## 11. Test Hints
- **Concurrent session limit:** Log in from 4 devices with limit = 3; verify the 4th is denied or oldest is revoked per strategy.
- **Idle timeout:** Set idle timeout to 2 minutes for testing; wait 3 minutes without activity; verify refresh returns 401.
- **Absolute timeout:** Set absolute timeout to 1 hour for testing; stay active; verify forced re-auth after 1 hour.
- **Admin session revocation:** Admin revokes a specific session; verify that session's refresh token is revoked and user is forced to re-auth.
- **Self session view:** User views sessions; verify correct device/browser info and "Current session" marking.
- **Self session revocation:** User revokes a non-current session; verify it is revoked. Attempt to revoke current session; verify it is blocked.
- **Cross-tenant isolation:** Verify session policies of tenant A do not affect sessions in tenant B.
- **Audit trail:** Verify all session management events are logged with correct metadata.
- **Background cleanup:** Verify expired tokens are cleaned up by the Hangfire job.
