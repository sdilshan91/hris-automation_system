---
id: US-NTF-006
module: Notifications & Audit
priority: Must Have
persona: Tenant Admin / System
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 8
---

# US-NTF-006: Notification Delivery Layer (SMTP Email + SignalR/In-App Dispatch)

> **Reconciliation story (COMPLETION-PLAN Theme B).** US-NTF-001/002/003 defined the in-app hub,
> per-tenant email templates, and per-user preferences, but the actual *delivery* was never wired:
> ~30 `LogOnly*` seams across every module only write to Serilog and nothing is ever sent. This story
> builds the real dispatch layer (SMTP sender + SignalR/in-app persistence) behind the existing
> `INotificationDispatcher` seam and rewires the stubs. It is the single largest completeness gap and
> is a hard dependency of the delivery ACs deferred on ~25 `[x]`-done stories (see STATUS.md notes).

## 1. Description
**As a** Tenant Admin (and, transitively, every user who is a notification recipient),
**I want to** have the platform actually deliver notifications through real channels — transactional
email via SMTP and in-app/real-time messages via the SignalR hub — instead of only logging them,
**So that** the delivery acceptance criteria promised across Auth, Core-HR, Leave, Attendance,
Recruitment, Payroll and Performance are genuinely met and recipients are actually notified.

## 2. Preconditions
- The in-app notification persistence + hub (US-NTF-001), per-tenant email templates (US-NTF-002),
  and per-user notification preferences (US-NTF-003) exist.
- The `INotificationDispatcher` seam and its `LogOnly*` default implementations are registered in DI.
- SMTP transport configuration is available per environment (host/port/credentials in secrets, or a
  per-tenant custom SMTP override where configured).
- A background-job runner (Hangfire on PostgreSQL) is available for asynchronous send + retry.

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A domain event that names an email AC fires (e.g. password-reset requested, payslip published, offer sent, leave approved) | The producing handler calls `INotificationDispatcher` | A real email is enqueued to the SMTP sender (not just logged); on successful SMTP handoff the notification row is marked `sent`, and the Serilog line is retained as an audit trail — the `LogOnly*` default is no longer the wired implementation in non-test environments. |
| AC-2 | The same event fires and the recipient is connected | Dispatch runs | An in-app notification row is persisted (US-NTF-001 schema) and pushed over SignalR to the recipient's tenant/user-scoped group within the NFR-1 latency budget. In-app and email are independent channels selected per notification type. |
| AC-3 | An SMTP send fails transiently (timeout / 4xx-greylist / connection reset) | The send job runs | The job retries with exponential backoff up to N attempts; after the final failure the notification is marked `failed` with the last error, and a dead-letter/audit record is written. No exception escapes to the originating request thread (dispatch is async/fire-and-forget from the producer's perspective). |
| AC-4 | A tenant has a configured email template for the notification type (US-NTF-002) | An email of that type is dispatched | The template is rendered with the event's model variables (tenant-scoped), including subject and HTML/plaintext bodies; if no tenant template exists, the platform default template for that type is used. |
| AC-5 | A recipient has opted out of a channel/type in their preferences (US-NTF-003) | A notification of that type is dispatched to them | The opted-out channel is suppressed for that recipient while other channels still deliver; transactional/security-critical types (password reset, account lockout, break-glass) are non-suppressible and always send. |
| AC-6 | User A in Tenant A and User B in Tenant B both have pending notifications | Dispatch runs for each | Emails use each tenant's own sender identity/template and SignalR pushes only to the recipient's own `t:{tenantId}:user:{userId}` group; no email address, template, or in-app message crosses a tenant boundary. |
| AC-7 | The ~30 previously `LogOnly*` producer seams exist across modules | The delivery layer is deployed | Each seam now resolves the real dispatcher and is exercised (Auth reset/lockout/break-glass; Core-HR doc-expiry/probation/import/manager-reassignment; Leave queue/approval; PAY-011 payslip email; PAY-003/008 & PRF-001/002/003/005/008/009 notifications; REC-002/004/005/006/007/008 emails; ATT-004/008/010 alerts; impersonation; export-ready). No producer still hard-codes a log-only path in production DI. |
| AC-8 | A bulk send is requested (e.g. PAY-011 payslip distribution to all employees) | The batch runs | Sends are fanned out as individual retryable jobs (one recipient per job), rate-limited per tenant, and a per-batch summary (queued/sent/failed counts) is recorded; a single bad recipient does not fail the batch. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: Provide a real `IEmailSender` (SMTP via `MailKit`/`System.Net.Mail`) implementation and register it as the wired `INotificationDispatcher` email channel, replacing `LogOnly*` in non-test environments.
- FR-2: All email sends SHALL be enqueued as Hangfire jobs (asynchronous, out of the request path) with idempotency keys to avoid duplicate sends on retry.
- FR-3: Implement retry with exponential backoff (configurable max attempts + base delay); terminal failures move to a dead-letter/failed state with the last error captured.
- FR-4: Render email bodies from the per-tenant template store (US-NTF-002) with a platform-default fallback per notification type; support both HTML and plaintext parts.
- FR-5: Respect per-user, per-channel, per-type preferences (US-NTF-003) at dispatch time; maintain a hard-coded non-suppressible list for security/transactional types.
- FR-6: Persist and push in-app notifications via the existing SignalR hub (US-NTF-001) as a parallel channel; channel selection per notification type is table-driven.
- FR-7: Support per-tenant SMTP override (custom sender domain/credentials) with the platform SMTP as default; tenant SMTP secrets are stored encrypted and never returned to the client.
- FR-8: Expose a dispatch API/facade (`INotificationDispatcher.DispatchAsync(notification, channels)`) that all ~30 producer seams call; remove the log-only wiring from production composition.
- FR-9: Record delivery status transitions (`queued → sent | failed`) with timestamps and error text for observability and troubleshooting.
- FR-10: Provide a bulk-dispatch entry point that fans a recipient list into individual retryable jobs with per-tenant rate limiting and a batch summary.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: In-app delivery latency SHALL be ≤ 2s from event to client (consistent with US-NTF-001 NFR-1); email hand-off to SMTP SHALL not block the originating request.
- NFR-2: All notification, delivery-status, and template data SHALL be tenant-isolated (EF query filters + RLS once enabled).
- NFR-3: Email sending SHALL be rate-limited per tenant to avoid SMTP-provider throttling and to prevent one tenant's bulk send from starving others.
- NFR-4: Tenant SMTP credentials and any stored secrets SHALL be encrypted at rest (see US-PLT-005) and redacted from logs.
- NFR-5: A downstream SMTP outage SHALL degrade gracefully — jobs queue and retry rather than throwing to users; the app remains functional without email.
- NFR-6: The delivery layer SHALL emit metrics (queued/sent/failed counts, send latency) suitable for the observability story (US-PLT-004).

## 6. Business Rules
- BR-1: Security/transactional notifications (password reset, account lockout, break-glass/impersonation alert) are always delivered and cannot be opted out.
- BR-2: A notification type maps to zero or more channels (email, in-app); the mapping is configuration, not code, per type.
- BR-3: Email uses the recipient tenant's template and sender identity; cross-tenant template/sender use is forbidden.
- BR-4: A failed send after max retries is surfaced (failed state + audit) — it is never silently dropped.
- BR-5: Bulk sends are per-recipient jobs; batch success is measured by per-recipient status, not all-or-nothing.

## 7. Data Requirements
- Reuses the `notification` table (US-NTF-001) plus a delivery-status column set (`channel`, `status`, `attempts`, `last_error`, `sent_at`) or a companion `notification_delivery` table.
- Reuses the per-tenant email template store (US-NTF-002) and per-user preference store (US-NTF-003).
- Per-tenant SMTP settings (host/port/from/credentials-encrypted) on the tenant settings surface.
- Input: notification type, recipient(s), model variables. Output: rendered email + persisted/pushed in-app row + delivery-status records.

## 8. UI/UX Notes
- No net-new primary UI; reuses the US-NTF-001 bell/panel for in-app.
- Admin-facing (optional, could-have): a delivery-log view showing recent sends and their status/errors for troubleshooting.
- Rendered emails follow the tenant's branding where templates provide it (US-NTF-002).

## 9. Dependencies
- US-NTF-001 (in-app hub + persistence), US-NTF-002 (templates), US-NTF-003 (preferences).
- Hangfire (background jobs) — already in the stack.
- US-PLT-005 (encryption-at-rest) for tenant SMTP secrets.
- US-PLT-004 (observability) consumes the emitted delivery metrics.
- Redis (US-PLT infra) improves SignalR backplane + rate-limiting at multi-instance scale (US-NTF-001 FR-10) but is not strictly required for single-instance delivery.
- All producer modules whose delivery ACs are currently deferred (Auth, Core-HR, Leave, Attendance, Recruitment, Payroll, Performance).

## 10. Assumptions & Constraints
- The `INotificationDispatcher` seam is designed as a one-class swap (per project notes ISSUE-188); this story provides the real class and flips DI — it does not redesign the producer call sites beyond wiring.
- SMTP provider/credentials are an ops-provisioned input; this story does not choose a provider, only integrates one.
- Only free/OSS libraries (e.g. MailKit) are used.
- This story does NOT build the message-template authoring UI (US-NTF-002) or the preference UI (US-NTF-003); it consumes them.

## 11. Test Hints
- **Wire-real:** assert the production DI resolves the SMTP sender, not `LogOnly*`; a test seam still uses a fake to avoid real sends.
- **Retry:** force an SMTP failure; verify backoff, attempt count, terminal `failed` state, no exception to the caller.
- **Template render:** dispatch with a tenant template present vs absent; verify tenant template used, else platform default.
- **Opt-out:** opt a user out of email for a type; verify suppression — then verify a security type still sends despite opt-out.
- **Tenant isolation:** dispatch for two tenants; verify each uses its own sender/template and SignalR group; no cross-tenant leak.
- **Bulk (PAY-011):** dispatch payslip email to N employees with one invalid address; verify N-1 sent, 1 failed, batch summary correct, rate limit honored.
- **Producer sweep:** for each rewired seam (reset, payslip, offer, leave-approval, etc.), trigger the event and assert a delivery-status row is created (not just a log line).
