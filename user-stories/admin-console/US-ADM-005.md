---
id: US-ADM-005
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ADM-005: Tenant Admin Manages Users and Role Assignments

## 1. Description
**As a** Tenant Admin,
**I want to** invite users to my tenant workspace, manage their status (activate/deactivate), assign or revoke roles, force password resets, and end user sessions,
**So that** I can control who has access to my organization's HR data and ensure each person has the appropriate permissions, all within the strict boundary of my own tenant.

## 2. Preconditions
- The Tenant Admin is authenticated on `{subdomain}.yourhrm.com` with an active `user_tenant` membership and the `TenantAdmin` role.
- The tenant is in `active` or `trial` status.
- The tenant has not exceeded its plan limit for maximum active users.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The Tenant Admin navigates to the Users section | The page loads | A paginated, searchable user list is displayed showing: display name, email, assigned roles, membership status (`invited`/`active`/`disabled`), last login timestamp, and linked employee record (if any). The list is scoped exclusively to the current tenant — no users from other tenants are visible. |
| AC-2 | The Tenant Admin clicks "Invite User" | They enter an email address (or multiple via CSV upload), select one or more roles, and submit | A `user_invitation` record is created with a one-time token (72-hour expiry), an invitation email is sent to each address using the tenant's email template, and the invitation appears in the "Pending Invitations" tab. If the email already exists as a global user, the system creates a new `user_tenant` membership (not a duplicate user). If the tenant's user count would exceed the plan limit, the invitation is rejected with "User limit reached for your plan." |
| AC-3 | The Tenant Admin selects a user and clicks "Edit Roles" | They add or remove roles from the user's membership and save | The `user_tenant_role` records are updated accordingly. The change is recorded in the tenant's `audit_log` with before/after state. The user's current JWT remains valid until expiry, but the next token refresh reflects the updated roles. |
| AC-4 | The Tenant Admin deactivates a user | They click "Deactivate" and confirm | The `user_tenant.status` is set to `disabled`, all refresh tokens for that user in this tenant are revoked, the user can no longer log in to this tenant (but their memberships in other tenants are unaffected), and the action is recorded in the tenant's `audit_log`. |
| AC-5 | The Tenant Admin clicks "Force Password Reset" for a user | The action is confirmed | All refresh tokens for that user (across all tenants, since the password is global) are revoked, the user's `password_changed_at` is nullified to force reset, a password-reset email is sent, and the action is logged. |
| AC-6 | The Tenant Admin attempts to view or manage users from another tenant by manipulating API request parameters (e.g., changing `tenant_id` in the request body or URL) | The API processes the request | The request is rejected. EF Core global query filters and PostgreSQL RLS ensure that no cross-tenant user data is returned or modified. The API returns 404 (not 403, to avoid existence disclosure). |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The user list endpoint shall return `user_tenant` records joined with `users` and `user_tenant_role`, filtered by the current `ITenantContext.TenantId`. Pagination (default 20, max 100), search (name, email), and filter (status, role) shall be supported.
- FR-2: The invite endpoint shall accept a single email or an array (bulk CSV). For each email, it shall: check global user existence, check existing tenant membership, validate against plan user limit, create `user_invitation`, and dispatch the invitation email.
- FR-3: Bulk CSV invite shall accept a file with columns: `email`, `role` (comma-separated role names). Validation errors are returned per-row without blocking valid rows.
- FR-4: Role assignment changes shall be persisted in `user_tenant_role` with `assigned_at` and `assigned_by` fields.
- FR-5: The "End All Sessions" action shall revoke all refresh tokens for the selected user within the current tenant only.
- FR-6: The user detail view shall show: profile info, assigned roles, linked employee record, audit trail (recent actions by/on this user), active sessions, and invitation history.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The user list shall load within 1.5 seconds for tenants with up to 5,000 users.
- NFR-2: All user management actions shall be audited to the tenant's `audit_log` with actor, action, before/after state, IP, and timestamp.
- NFR-3: Invitation emails shall be dispatched within 30 seconds of submission.
- NFR-4: The user management UI shall be 100% mobile responsive (360px to 4K) following the Notion-like design aesthetic.
- NFR-5: Cross-tenant data isolation shall be enforced at three layers: application (ITenantContext), EF Core (global query filter), and PostgreSQL (RLS).

## 6. Business Rules
- BR-1: A Tenant Admin can manage only users within their own tenant. They cannot see or affect users' memberships in other tenants.
- BR-2: The Tenant Owner role cannot be removed by a Tenant Admin (only transferred via a separate ownership transfer flow).
- BR-3: A Tenant Admin cannot deactivate their own account (self-deactivation prevention).
- BR-4: Built-in roles (e.g., `TenantAdmin`, `TenantOwner`, `Employee`, `Manager`, `HR Officer`, `Auditor`) cannot be edited or deleted, but can be assigned/unassigned.
- BR-5: Plan user limits are enforced at invitation time, not at login time. If a tenant downgrades their plan, existing users beyond the new limit remain active but no new invitations are allowed.
- BR-6: Invitation tokens expire after 72 hours. Expired invitations can be resent (new token generated).
- BR-7: A user can have multiple roles in the same tenant (e.g., `Manager` + `HR Officer`).

## 7. Data Requirements
- **Input (invite):** `email` (string or array), `role_ids` (UUID array).
- **Input (role edit):** `user_tenant_id` (UUID), `role_ids` (UUID array — new complete set).
- **Output (user list):** Array of user summaries with pagination metadata.
- **Tables affected:** `users`, `user_tenant`, `user_tenant_role`, `user_invitation`, `refresh_token`, `audit_log`.

## 8. UI/UX Notes
- User list: Angular Material data table with avatar, name, email, role chips, status badge, and last-login timestamp. Search bar at top, filter dropdowns for status and role.
- Invite modal: email input with tag-style entry (type and press Enter to add multiple), role multi-select dropdown, CSV upload dropzone.
- Role editor: checkbox list of available roles with descriptions, shown in a side panel or modal.
- Deactivation: confirmation dialog with the user's name prominently displayed.
- Pending Invitations tab: shows email, invited roles, invited at, expires at, status, with "Resend" and "Revoke" actions.
- Notion-like aesthetic: clean cards, subtle borders, smooth hover transitions, consistent spacing.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned with at least the Tenant Owner user.
- Authentication system (JWT, refresh tokens).
- Email service for invitation dispatch.
- Audit logging infrastructure.
- Role and permission seed data (built-in roles).

## 10. Assumptions & Constraints
- Custom roles (clone & edit permission set) are deferred to Phase 2. Phase 1 uses only built-in roles.
- Auto-assign roles based on department/job title is deferred to Phase 2.
- SCIM provisioning is deferred to Phase 2.
- The `users` table is global (not tenant-scoped); user management in this context means managing `user_tenant` memberships and role assignments within the tenant.

## 11. Test Hints
- **Invite new user:** Invite a user who does not exist globally; verify `users` record created, `user_tenant` created with `status = 'invited'`, invitation email sent.
- **Invite existing global user:** Invite a user who exists in another tenant; verify no duplicate `users` record, new `user_tenant` created.
- **Plan limit enforcement:** Set plan `max_employees = 5`, invite 5 users (success), attempt 6th (expect rejection with plan limit message).
- **Role assignment:** Assign `Manager` + `HR Officer` roles; verify both `user_tenant_role` records exist and audit log records the change.
- **Deactivation isolation:** Deactivate a user in Tenant A; verify they can still log in to Tenant B.
- **Cross-tenant isolation:** Authenticate as Tenant A admin, attempt to list Tenant B users via API parameter manipulation; verify 404 or empty result.
- **Self-deactivation prevention:** Attempt to deactivate the currently logged-in Tenant Admin; verify rejection.
- **Bulk CSV invite:** Upload a CSV with 5 valid and 2 invalid emails; verify 5 invitations created and 2 errors returned per-row.
- **Invitation expiry:** Create an invitation, advance time past 72 hours, attempt to accept; verify rejection. Resend and verify new token works.
