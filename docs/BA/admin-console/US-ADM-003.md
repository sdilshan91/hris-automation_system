---
id: US-ADM-003
module: Admin Console — System Admin
priority: Must Have
persona: System Admin (Platform Operator Staff)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ADM-003: System Admin Impersonates Tenant User (With Audit)

## 1. Description
**As a** System Admin (Platform Operator Staff),
**I want to** impersonate a specific user within a tenant's workspace by providing a mandatory reason,
**So that** I can investigate reported issues, reproduce bugs, and provide support without requiring the tenant user's credentials, while maintaining a complete and immutable audit trail of every action taken during the impersonation session.

## 2. Preconditions
- The System Admin is authenticated at `admin.yourhrm.com` with the `SystemAdmin` role.
- The target tenant exists and is not in `terminated` status.
- The target user has an active membership (`user_tenant.status = 'active'`) in the target tenant.
- The impersonation feature is not disabled by a feature flag or system policy.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The System Admin is on the tenant detail view and selects "Impersonate User" | They choose a target user from the tenant's user list and provide a mandatory reason (min 10 characters) | The system generates a time-limited impersonation JWT (max 60 minutes) containing: the original System Admin's `user_id` as `impersonator_id`, the target user's `user_id` as `sub`, the target `tenant_id`, and an `impersonation_session_id`. The System Admin's browser opens the tenant's subdomain URL in a new tab with the impersonation token. |
| AC-2 | The System Admin is in an active impersonation session | They perform any action (view, create, update) within the tenant | Every action is recorded in both the tenant's `audit_log` (with `actor_type = 'impersonated'` and `impersonator_id` fields) and the `system_audit_log`. The tenant UI displays a persistent, non-dismissable banner: "This session is being operated by platform support (ref: {session_id})". |
| AC-3 | The System Admin is in an impersonation session | The 60-minute session expires or the System Admin clicks "End Impersonation" | The impersonation JWT is revoked, the session terminates, the System Admin is returned to the admin console, and a session-end event is written to both audit logs with a summary of actions taken. |
| AC-4 | A System Admin initiates an impersonation session | The system processes the request | An automatic notification (email and/or in-app) is sent to the tenant's Tenant Admin(s) informing them: "Platform support user {name} has started an impersonation session as {target_user} at {timestamp}. Reason: {reason}. Reference: {session_id}." |
| AC-5 | A System Admin attempts to impersonate a user in a `suspended` tenant | They submit the impersonation request | The system allows the impersonation (since the System Admin may need to investigate suspension-related issues) but the impersonated session operates in read-only mode with no write API access. |
| AC-6 | A user with `SystemSupport` role attempts to impersonate | They initiate an impersonation request | The system allows it with read-only access only. Write operations return 403 with message "Support impersonation is read-only." |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The impersonation initiation endpoint shall accept: `target_user_id` (UUID), `target_tenant_id` (UUID), `reason` (string, min 10 chars, max 500 chars). It shall return an impersonation JWT and a redirect URL.
- FR-2: The impersonation JWT shall contain additional claims: `imp_session_id` (UUID), `imp_actor_id` (original System Admin user_id), `imp_reason` (truncated), `imp_expires_at`. Standard tenant claims (`tenant_id`, roles of the target user) are also included.
- FR-3: The backend authorization middleware shall detect impersonation tokens and: (a) enforce the impersonation expiry, (b) tag all audit entries with the impersonator identity, (c) restrict destructive operations (delete tenant, delete user, change passwords) even for `SystemAdmin` impersonators.
- FR-4: The impersonation session shall be trackable in a dedicated `impersonation_session` record with: session_id, impersonator_user_id, target_user_id, target_tenant_id, reason, started_at, ended_at, actions_count, status (active/ended/expired).
- FR-5: The tenant admin notification shall use the system notification template `impersonation_started` and be dispatched via both email and in-app channels.
- FR-6: During impersonation, the System Admin shall not be able to: change tenant user passwords, modify roles/permissions, delete users, or access other tenants' data from within the impersonated session.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Impersonation audit records shall be immutable (append-only table; application DB role has no UPDATE/DELETE permission on audit tables).
- NFR-2: The impersonation JWT shall have a maximum TTL of 60 minutes and cannot be refreshed — a new impersonation session must be initiated.
- NFR-3: Impersonation session initiation shall complete in under 2 seconds.
- NFR-4: The impersonation banner in the tenant UI shall be rendered by a global Angular component that cannot be hidden or overridden by any tenant-level customization or CSS.
- NFR-5: All impersonation events shall be tagged with `traceId` for end-to-end correlation in the observability stack.

## 6. Business Rules
- BR-1: Only `SystemAdmin` and `SystemSupport` roles may initiate impersonation sessions. `SystemSupport` sessions are always read-only.
- BR-2: A System Admin cannot impersonate another System Admin (system-tenant users are excluded from impersonation targets).
- BR-3: Only one active impersonation session per System Admin is allowed at a time.
- BR-4: The reason field is mandatory and must contain at least 10 meaningful characters. It is stored verbatim and included in the tenant admin notification.
- BR-5: Impersonation of users in `terminated` tenants is not allowed.
- BR-6: The impersonation banner must display in all languages supported by the tenant (i18n).

## 7. Data Requirements
- **Input:** `target_user_id` (UUID), `target_tenant_id` (UUID), `reason` (string).
- **Output:** Impersonation JWT, redirect URL, `impersonation_session_id` (UUID).
- **New table/entity:** `impersonation_session` (session_id, impersonator_user_id, target_user_id, target_tenant_id, reason, started_at, ended_at, actions_count, status).
- **Audit entries:** Written to both `audit_log` (tenant-scoped, with `impersonator_id` field) and `system_audit_log`.

## 8. UI/UX Notes
- The "Impersonate User" button appears in the tenant detail view next to the user list, styled as a secondary action with a distinctive icon (e.g., a mask or user-switch icon).
- The reason field is a textarea with character counter, shown in a confirmation modal: "You are about to impersonate {user_name} in {tenant_name}. Provide a reason for this action."
- During impersonation, the tenant UI shows a fixed top banner (high-contrast, e.g., orange background): "SUPPORT SESSION -- Operated by platform support (Ref: {session_id}) -- [End Session]".
- The "End Session" button in the banner immediately terminates the impersonation and redirects to the admin console.
- Notion-like design: the confirmation modal uses clean typography, adequate whitespace, and a clear call-to-action hierarchy.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned.
- US-ADM-005: Tenant user management (target users must exist).
- Audit logging infrastructure (Section 24 of technical document).
- Notification system for tenant admin alerts.
- JWT token service must support impersonation claims.

## 10. Assumptions & Constraints
- Impersonation is a Phase 1 feature due to its criticality for customer support.
- The impersonation JWT is a separate token type, not a modified version of the user's existing token.
- Browser-based impersonation only; API-level impersonation (e.g., via API keys) is not supported.
- The tenant admin cannot block or opt out of impersonation (it is a platform-level support capability), but they are always notified.

## 11. Test Hints
- **Happy path:** Impersonate a tenant user, perform a read action, verify the action is logged in both tenant audit log and system audit log with correct impersonator attribution.
- **Reason validation:** Attempt impersonation with fewer than 10 characters in the reason field; verify rejection.
- **Expiry:** Start an impersonation session, wait for expiry (or mock time advance); verify the token is rejected after 60 minutes.
- **Tenant admin notification:** Impersonate a user; verify the tenant admin receives an email and in-app notification with session details.
- **Read-only for suspended tenants:** Impersonate a user in a suspended tenant; attempt a write operation; verify 403.
- **SystemSupport read-only:** Authenticate as SystemSupport, impersonate a user, attempt a write; verify 403.
- **Cannot impersonate system admin:** Attempt to impersonate a user in the system tenant; verify rejection.
- **Tenant isolation:** During impersonation in Tenant A, attempt to access Tenant B data; verify 404.
- **Concurrent sessions:** Attempt to start a second impersonation session while one is active; verify rejection.
- **Audit immutability:** Attempt to UPDATE or DELETE records in the audit log table using the application DB role; verify the operation is denied.
