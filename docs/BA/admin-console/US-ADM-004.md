---
id: US-ADM-004
module: Admin Console — System Admin
priority: Must Have
persona: System Admin (Platform Operator Staff)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ADM-004: System Admin Suspends or Terminates a Tenant

## 1. Description
**As a** System Admin (Platform Operator Staff),
**I want to** suspend or terminate a tenant's workspace by specifying a reason and, for termination, a grace period,
**So that** I can enforce platform policies (ToS violations, payment failures) while ensuring tenants have fair notice, data export opportunities during grace, and that all lifecycle transitions are immutably audited.

## 2. Preconditions
- The System Admin is authenticated at `admin.yourhrm.com` with the `SystemAdmin` role.
- The target tenant exists and is in a state that permits the requested transition (see Business Rules).
- The notification system is operational for lifecycle emails.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The System Admin is viewing a tenant in `active` or `past_due` status | They click "Suspend Tenant", provide a mandatory reason (min 10 chars), and confirm via a two-step confirmation dialog | The tenant's status changes to `suspended`, `suspended_at` is set to the current timestamp, `suspended_reason` is stored, all tenant user sessions are revoked (refresh tokens invalidated), background jobs for the tenant are paused, API access returns HTTP 451 for tenant users, and a `tenant_lifecycle_event` with `event_type = 'suspended'` is written. A suspension notification email is sent to the tenant's billing email and all Tenant Admin users. |
| AC-2 | A suspended tenant's users attempt to log in | They enter valid credentials on `{subdomain}.yourhrm.com` | Only Tenant Admin users are allowed to log in. They see a read-only suspension notice page showing: the suspension reason, a "Contact Support" link, and an option to request data export. All other tenant users are blocked from login with the message "Your organization's account has been suspended. Please contact your administrator." |
| AC-3 | The System Admin selects "Terminate Tenant" for an `active`, `past_due`, or `suspended` tenant | They provide a reason, set a grace period (default 30 days, plan-configurable), and confirm via a typed-confirmation dialog ("Type the tenant subdomain to confirm") | The tenant status changes to `terminating`, `termination_scheduled_at` is set (current time + grace period), the tenant enters read-only mode with export endpoints active, reminder emails are scheduled at 14d, 7d, and 1d before data deletion, and a `tenant_lifecycle_event` with `event_type = 'termination_initiated'` is recorded. |
| AC-4 | A tenant is in `terminating` status and the grace period expires | The scheduled Hangfire job fires | All per-tenant data is hard-deleted (`DELETE FROM ... WHERE tenant_id = X` for all business tables), the `tenant` record is retained with `status = 'terminated'` and PII fields redacted, audit logs are retained per the retention policy, and a `tenant_lifecycle_event` with `event_type = 'terminated'` is recorded. The operation is logged in `system_audit_log`. |
| AC-5 | The System Admin views a tenant in `suspended` status | They click "Reactivate Tenant" | The tenant status reverts to `active` (or `past_due` if payment issue persists), `suspended_at` and `suspended_reason` are cleared, background jobs are resumed, users can log in normally, and a reactivation notification is sent to the tenant's Tenant Admin users. A `tenant_lifecycle_event` with `event_type = 'reactivated'` is recorded. |
| AC-6 | The System Admin views a tenant in `terminating` status (within grace period) | They click "Restore Tenant" | The tenant status reverts to `suspended` or `active` (based on prior state), `termination_scheduled_at` is cleared, scheduled reminder emails are cancelled, the data deletion job is de-queued, and a `tenant_lifecycle_event` with `event_type = 'restored'` is recorded. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The suspension endpoint shall accept: `tenant_id` (UUID), `reason` (string, 10-500 chars). It shall update the tenant record, revoke all refresh tokens for the tenant, and dispatch notifications.
- FR-2: The termination endpoint shall accept: `tenant_id` (UUID), `reason` (string), `grace_period_days` (int, min 7, max 90, default from plan). It shall update the tenant record, schedule the data deletion Hangfire job, and schedule reminder notification jobs.
- FR-3: The data deletion job shall delete from all per-tenant tables in dependency order (children first), within a transaction. The `tenant` record is updated to `terminated` with PII redaction, not deleted.
- FR-4: The typed-confirmation dialog for termination shall require the System Admin to type the tenant's subdomain exactly to proceed.
- FR-5: The reactivation endpoint shall reverse the suspension: update status, clear suspension fields, resume Hangfire jobs scoped to the tenant.
- FR-6: The restoration endpoint shall reverse the termination: update status, clear `termination_scheduled_at`, remove scheduled deletion and reminder jobs from Hangfire.
- FR-7: All state transitions shall write a `tenant_lifecycle_event` record and a `system_audit_log` entry.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Suspension shall take effect within 30 seconds of confirmation (session revocation, API blocking).
- NFR-2: Data deletion for terminated tenants shall complete within 10 minutes for tenants with up to 50,000 employee records.
- NFR-3: Data deletion shall be executed during low-traffic hours (configurable maintenance window) to minimize database load.
- NFR-4: All lifecycle transitions must be atomic; partial state changes are not acceptable.
- NFR-5: The typed-confirmation dialog must prevent paste operations and require manual typing for security.

## 6. Business Rules
- BR-1: Valid state transitions: `active` -> `suspended`, `past_due` -> `suspended`, `active|past_due|suspended` -> `terminating`, `suspended` -> `active|past_due`, `terminating` -> `suspended|active` (restore within grace).
- BR-2: The system tenant (`is_system = true`) cannot be suspended or terminated.
- BR-3: A tenant in `terminated` status cannot be restored through the UI. Recovery from backup requires a manual operator process.
- BR-4: Grace period minimum is 7 days (to allow data export), maximum is 90 days.
- BR-5: Suspension revokes all existing refresh tokens for the tenant but preserves user data and configuration.
- BR-6: During `terminating` state, only GET (read) and export endpoints are accessible. All write operations return 403.
- BR-7: Only `SystemAdmin` role can suspend/terminate tenants. `SystemSupport` can view lifecycle history but cannot initiate transitions.

## 7. Data Requirements
- **Input (suspend):** `tenant_id`, `reason`.
- **Input (terminate):** `tenant_id`, `reason`, `grace_period_days`.
- **Output:** Updated tenant record, lifecycle event.
- **Tables affected (lifecycle):** `tenant` (status update), `tenant_lifecycle_event` (new record), `system_audit_log` (new record), `refresh_token` (revocation).
- **Tables affected (data deletion):** All per-tenant business tables (employees, leave, attendance, payroll, etc.), `user_tenant` (removed), `audit_log` (retained per policy), `tenant_setting`, file storage paths.

## 8. UI/UX Notes
- Suspension: a modal dialog with reason textarea, clear warning text ("This will immediately block all tenant users from accessing the platform"), and a red "Suspend" button.
- Termination: a multi-step confirmation: (1) reason + grace period input, (2) impact summary (employee count, storage used, data to be deleted), (3) typed subdomain confirmation. Final "Terminate" button is red and disabled until the subdomain is typed correctly.
- Reactivation/Restoration: simpler confirmation modals with green action buttons.
- Tenant detail view shows the current lifecycle state with a visual state indicator (color-coded badge) and full lifecycle history timeline.
- Follow Notion-like design: destructive actions use red styling; confirmations use adequate whitespace and clear typography.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned.
- US-ADM-010: Data export must be available for tenants in `terminating` state.
- Hangfire job scheduling for data deletion and reminder emails.
- Notification system for lifecycle emails.
- Refresh token revocation mechanism.

## 10. Assumptions & Constraints
- Phase 1: Suspension and termination are manual System Admin actions. Automated suspension due to payment failure (dunning) is deferred to Phase 2.
- Self-service cancellation by Tenant Admin is deferred to Phase 2.
- Data deletion is a hard delete (not soft delete); the `tenant` record is retained with PII redacted for audit.
- File storage (documents, resumes, payslips) associated with the tenant must also be deleted during termination.

## 11. Test Hints
- **Suspend active tenant:** Suspend an active tenant; verify status change, session revocation, API blocking (451), and notification dispatch.
- **Login during suspension:** Attempt login as a regular tenant user; verify blocked. Attempt login as Tenant Admin; verify allowed with read-only view.
- **Terminate with grace:** Initiate termination with 30-day grace; verify `termination_scheduled_at` is correctly set and reminder jobs are scheduled.
- **Data deletion:** Allow a termination grace period to expire (or trigger manually); verify all per-tenant data is deleted, `tenant` record is retained with `status = 'terminated'`, and PII is redacted.
- **Restore within grace:** Initiate termination, then restore within grace; verify status revert and deletion job cancellation.
- **Invalid transitions:** Attempt to suspend an already-suspended tenant; verify rejection. Attempt to terminate a `terminated` tenant; verify rejection.
- **System tenant protection:** Attempt to suspend/terminate the system tenant; verify rejection.
- **Tenant isolation during deletion:** During data deletion of Tenant A, verify Tenant B's data is completely unaffected (run queries against Tenant B before and after).
- **Typed confirmation:** Attempt termination with incorrect subdomain in the confirmation field; verify the action is blocked.
