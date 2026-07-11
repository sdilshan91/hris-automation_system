---
id: US-ADM-008
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-008: Tenant Admin Views Audit Logs

## 1. Description
**As a** Tenant Admin,
**I want to** view, search, and filter the audit log for my tenant, covering all write operations, authentication events, PII access events, settings changes, and exports,
**So that** I can maintain accountability, investigate incidents, fulfill compliance requirements (e.g., GDPR), and have a tamper-proof record of all significant actions within my organization.

## 2. Preconditions
- The Tenant Admin is authenticated on `{subdomain}.yourhrm.com` with the `TenantAdmin` or `Auditor` role.
- The tenant is in `active` or `trial` status.
- The `audit_log` table contains records for the current tenant (populated by the EF Core `SaveChangesInterceptor`).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The Tenant Admin navigates to the Audit Log section | The page loads | A paginated, reverse-chronological list of audit records is displayed, scoped exclusively to the current tenant. Each entry shows: timestamp, actor (user name + email), action (e.g., "Employee.Create", "Leave.Approve", "User.Deactivate"), resource type, resource ID, IP address, and a summary of changes. No records from other tenants are visible. |
| AC-2 | The Tenant Admin applies filters | They filter by: date range, actor (user), action type, resource type, and keyword search in the change summary | The list updates to show only matching records. Filters can be combined (AND logic). The filtered results maintain pagination and sorting. |
| AC-3 | The Tenant Admin clicks on an audit record to view details | The detail panel opens | The full audit record is displayed, including: `before` state (JSON), `after` state (JSON), diff highlight (changed fields), IP address, user agent, and trace ID. Sensitive field values (e.g., password hashes, bank account numbers) are masked in the display. |
| AC-4 | The Tenant Admin exports the audit log | They click "Export" with the current filters applied | A CSV or JSON file is generated containing the filtered audit records (sensitive values masked). The export is itself logged as an audit event ("AuditLog.Export"). The file is available for download immediately for small datasets or via email link for large datasets (Hangfire job). |
| AC-5 | The Tenant Admin (or any user) attempts to modify or delete an audit record | They attempt via API | The operation is rejected. The `audit_log` table is append-only; the application database role lacks UPDATE and DELETE permissions on this table. Any such attempt is logged as a security event. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The audit log list endpoint shall query the `audit_log` table filtered by `tenant_id = ITenantContext.TenantId`, with support for: pagination (default 50, max 200), sorting (timestamp desc by default), and filtering by `timestamp` range, `actor_user_id`, `action`, `resource_type`, and full-text search on `before`/`after` JSON.
- FR-2: Each audit record shall contain: `audit_id`, `timestamp`, `tenant_id`, `actor_user_id`, `actor_employee_no`, `action`, `resource_type`, `resource_id`, `before` (JSON), `after` (JSON), `ip_address`, `user_agent`, `trace_id`.
- FR-3: The detail view shall compute a visual diff between `before` and `after` JSON, highlighting added, modified, and removed fields.
- FR-4: Sensitive fields (as defined in a configurable sensitive-fields list: `password_hash`, `mfa_secret`, `bank_account_number`, `national_id`) shall be masked in the audit log display as `***REDACTED***`.
- FR-5: The export functionality shall respect current filters and produce a downloadable file. For datasets > 10,000 records, the export shall be processed as a Hangfire background job with the download link emailed to the requesting user.
- FR-6: Audit log retention shall be governed by the tenant's plan (`audit_log_retention_days`). Records older than the retention period are purged by a scheduled Hangfire job. The purge itself is logged in `system_audit_log`.
- FR-7: The `Auditor` role has read-only access to audit logs but cannot export or access other admin functions.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The audit log list shall load within 2 seconds for the first page (50 records), even for tenants with millions of audit records.
- NFR-2: The `audit_log` table shall be indexed on: `(tenant_id, timestamp DESC)`, `(tenant_id, actor_user_id)`, `(tenant_id, action)`, `(tenant_id, resource_type, resource_id)`.
- NFR-3: The audit log is immutable: the application DB role has no UPDATE or DELETE grants on the `audit_log` table.
- NFR-4: Audit log queries shall not block or slow down write operations on business tables.
- NFR-5: The audit log UI shall be 100% responsive (360px to 4K) with the Notion-like design aesthetic.

## 6. Business Rules
- BR-1: Only `TenantAdmin`, `TenantOwner`, and `Auditor` roles may view the tenant audit log.
- BR-2: The `Auditor` role has read-only access to the audit log and reports; they cannot modify any data.
- BR-3: Audit log records are scoped strictly to the tenant. The `system_audit_log` (covering cross-tenant operations, impersonation, etc.) is visible only to System Admins.
- BR-4: Audit log exports are themselves audited as an action ("AuditLog.Export") to prevent silent data exfiltration.
- BR-5: Audit log retention is plan-dependent (e.g., Starter: 90 days, Business: 365 days, Enterprise: 7 years). The Tenant Admin can view their retention period but cannot change it (it is plan-governed).
- BR-6: PII access events (reads of sensitive employee fields like national ID, bank account) are logged and visible in the audit log.

## 7. Data Requirements
- **Input (filters):** `start_date` (date), `end_date` (date), `actor_user_id` (UUID), `action` (string), `resource_type` (string), `search_query` (string).
- **Output:** Paginated array of audit records with metadata (total count, page info).
- **Source table:** `audit_log` (tenant-scoped, append-only).
- **Export format:** CSV (columns: timestamp, actor, action, resource_type, resource_id, summary, ip_address) or JSON Lines.

## 8. UI/UX Notes
- Audit log page: a full-width data table with columns: Timestamp, User, Action, Resource, IP Address, Summary. Each row is expandable (accordion) or clickable to open a side panel with the full diff view.
- Filter bar: date range picker, user selector (autocomplete), action type dropdown (populated from distinct actions in the log), resource type dropdown, and a search text input.
- Diff view: side-by-side or inline diff with color-coded changes (green for added, red for removed, yellow for modified).
- Export button: top-right, with format selection (CSV/JSON) and a confirmation dialog showing the record count.
- Retention indicator: a subtle info badge showing "Records retained for {X} days per your plan."
- Notion-like aesthetic: clean table layout, smooth expand/collapse animations, subtle hover effects, and clear typography.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned.
- Audit logging infrastructure (EF Core `SaveChangesInterceptor`).
- Background job support (Hangfire) for large exports and retention purge.
- Email service for export download link delivery.

## 10. Assumptions & Constraints
- The audit log is stored in the same PostgreSQL database as business data (not a separate service). For very high-volume tenants, consider partitioning by month (Phase 2).
- Streaming export to ELK/Splunk for long-term storage is handled by the infrastructure team and is outside the scope of this story.
- The audit log does not capture read operations on non-sensitive fields (to avoid excessive volume). Only PII reads, all writes, and auth events are logged.
- The `system_audit_log` is a separate table/context and is not accessible from the tenant admin console.

## 11. Test Hints
- **Tenant isolation:** Create audit events in Tenant A and Tenant B; query as Tenant A admin; verify only Tenant A records are returned.
- **Filter accuracy:** Create multiple audit events with different actors, actions, and timestamps; apply each filter individually and in combination; verify correct results.
- **Diff view:** Update an employee record (change name from "John" to "Jane"); view the audit entry; verify the diff shows the old and new values correctly.
- **Sensitive field masking:** Trigger an audit event that includes a bank account number in the `before`/`after` JSON; verify it is displayed as `***REDACTED***`.
- **Immutability:** Attempt to execute `UPDATE audit_log SET action = 'Tampered' WHERE audit_id = X` using the application DB role; verify the operation is denied.
- **Export audit:** Export the audit log; verify an "AuditLog.Export" event is created in the audit log.
- **Large export:** Generate > 10,000 audit records; trigger export; verify it is processed as a background job and the download link is emailed.
- **Retention:** Set retention to 90 days; create an audit record dated 91 days ago; run the purge job; verify the old record is deleted but recent records are intact.
- **Auditor role:** Authenticate as Auditor; verify read access to audit log. Attempt to export; verify it is either allowed (read-only export) or denied per business rule.
