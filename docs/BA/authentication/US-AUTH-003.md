---
id: US-AUTH-003
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (all roles)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-AUTH-003: User logout and token invalidation

## 1. Description
**As a** tenant user,
**I want to** log out of my current session and have all associated tokens invalidated immediately,
**So that** no one can use my session after I leave, protecting my account and organizational data.

## 2. Preconditions
- The user is currently authenticated with a valid JWT access token and refresh token.
- The user has an active session in a specific tenant context.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user is authenticated with a valid session | They click "Log out" or call `POST /api/v1/auth/logout` | The system revokes the current refresh token (sets `revoked_at`), clears the httpOnly refresh token cookie, and returns 200 OK. |
| AC-2 | A user has logged out | They attempt to use the previously issued access token | The access token remains technically valid until its ~15 min expiry (stateless JWT), but any attempt to refresh returns 401 since the refresh token is revoked. |
| AC-3 | A tenant admin revokes all sessions for a specific user via `POST /api/v1/tenant/users/{id}/sessions/revoke` | The target user attempts to refresh their token | All refresh tokens for that user within the tenant are revoked; the user is forced to re-authenticate on next refresh attempt. |
| AC-4 | A user logs out from one tenant context but has active sessions in other tenants | They access another tenant they belong to | Sessions in other tenants remain unaffected; only the current tenant session is invalidated. |
| AC-5 | A user triggers logout but the API call fails (network error) | The frontend handles the error | The frontend clears the local access token from memory and redirects to the login page regardless, ensuring client-side session cleanup even if server-side revocation is delayed. |

## 4. Functional Requirements
- FR-1: The logout endpoint SHALL be `POST /api/v1/auth/logout`.
- FR-2: The system SHALL revoke the refresh token associated with the current session by setting `revoked_at` in the `refresh_token` table.
- FR-3: The system SHALL clear the httpOnly refresh token cookie by setting it to an expired value with the same domain, path, and security attributes.
- FR-4: The system SHALL log a `logout` event in the tenant audit log with user ID, IP address, user agent, and timestamp.
- FR-5: Tenant admins SHALL be able to revoke all sessions for a user via `POST /api/v1/tenant/users/{id}/sessions/revoke`, which revokes all non-expired refresh tokens for that user-tenant pair.
- FR-6: System admins SHALL be able to revoke all sessions for a user across all tenants when performing security actions.
- FR-7: The Angular SPA SHALL clear the in-memory access token and any cached user/permission state on logout, then navigate to the login page.

## 5. Non-Functional Requirements
- NFR-1: Logout API response time SHALL be <= 200 ms at P95.
- NFR-2: Token revocation SHALL be durable (persisted to database) and not rely solely on in-memory state.
- NFR-3: The gap between logout and effective session termination SHALL be at most the access token lifetime (~15 minutes) for stateless JWT validation; consider Redis-based token blacklist for immediate invalidation of critical sessions (e.g., admin-initiated revocation).
- NFR-4: Audit log entry for logout SHALL be written synchronously before the response is returned.

## 6. Business Rules
- BR-1: Logout invalidates only the current tenant session; cross-tenant sessions remain independent.
- BR-2: An admin-initiated session revocation SHALL notify the affected user via in-app notification (if online) or email.
- BR-3: Impersonation sessions end when the impersonating system admin logs out; the impersonation log `ended_at` is set.
- BR-4: Batch session revocation (e.g., "revoke all sessions for all users") is a System Admin privilege only, used in security incidents.

## 7. Data Requirements
- **Input:** No body required for user-initiated logout; the refresh token is read from the httpOnly cookie.
- **Output:** `{ message: "Logged out successfully" }` with HTTP 200.
- **Database updates:** `refresh_token.revoked_at` set to current timestamp.
- **Audit record:** `{ event_type: "logout", user_id, tenant_id, ip_address, user_agent, timestamp }`.
- **Admin revocation input:** `POST /api/v1/tenant/users/{id}/sessions/revoke` with the target user_tenant_id in the path.

## 8. UI/UX Notes
- Notion-like minimal design: a user menu dropdown in the top-right with the user's avatar/initials and a "Log out" option at the bottom.
- On logout, smooth transition to the tenant-branded login page.
- If the admin revokes a user's session and the user is online, display a non-dismissible modal: "Your session has been ended by an administrator. Please log in again."
- No confirmation dialog for self-logout (low-friction exit).
- Mobile: logout accessible from the hamburger/sidebar menu.

## 9. Dependencies
- US-AUTH-001 (Login) and US-AUTH-002 (JWT/Refresh flow) must be implemented.
- US-AUTH-009 (Session management) for admin session revocation features.
- Tenant audit logging infrastructure.

## 10. Assumptions & Constraints
- Stateless JWT validation means a logged-out access token remains valid until natural expiry; this is an accepted trade-off for performance. For high-security scenarios, a Redis token blacklist can be added.
- The refresh token cookie path and domain must exactly match the login issuance to ensure proper clearing.
- The frontend is responsible for clearing in-memory state regardless of API call success.

## 11. Test Hints
- **Happy path logout:** Logout, verify refresh token is revoked, verify cookie is cleared, verify subsequent refresh returns 401.
- **Access token after logout:** Verify the stateless access token still works for its remaining lifetime (unless blacklist is implemented).
- **Admin revocation:** Admin revokes sessions; verify all refresh tokens for that user-tenant are revoked.
- **Cross-tenant isolation:** Logout from tenant A; verify session in tenant B is still valid.
- **Network failure:** Simulate API failure on logout; verify frontend still clears local state and redirects to login.
- **Audit trail:** Verify logout event is recorded with correct metadata.
- **Impersonation end:** System admin ends impersonation; verify impersonation_log.ended_at is set.
