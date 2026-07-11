---
id: US-NTF-004
module: Notifications & Audit
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-NTF-004: Audit Trail for All Data Changes

## 1. Description
**As a** Tenant Admin (or Auditor),
**I want** every create, update, and delete operation on business data within my tenant to be automatically recorded in an immutable audit log with before/after snapshots,
**So that** the organization has a complete, tamper-proof record of all changes for compliance, security investigations, and operational transparency.

## 2. Preconditions
- The tenant is active and the audit logging system is operational.
- EF Core `SaveChangesInterceptor` is configured for audit capture.
- The `audit_log` table exists with RLS policies enforcing tenant isolation.
- The database application role lacks UPDATE/DELETE permissions on the `audit_log` table (append-only).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer updates an employee's job title | The change is saved via EF Core | An audit record is automatically created capturing: timestamp, actor (user_id, employee_no), action ("Employee.Update"), resource type ("Employee"), resource ID, before state (`{"jobTitleId": "old-id"}`), after state (`{"jobTitleId": "new-id"}`), IP address, user agent, and trace ID. The `tenant_id` is set from the session context. |
| AC-2 | An employee's salary information is read by an HR Officer | The PII field is accessed | An audit record is created with action "Employee.ReadSensitive" capturing which sensitive fields (bank account, national ID) were accessed, the accessor's identity, and the trace ID. |
| AC-3 | A leave request is soft-deleted by HR | The `is_deleted` flag is set to true | An audit record captures the action "LeaveRequest.Delete" with before state showing `is_deleted: false` and after state showing `is_deleted: true`. |
| AC-4 | An authentication event occurs (login, logout, password change, MFA enroll) | The event is processed | An audit record is created with the auth-specific action type, actor identity, IP address, user agent, and success/failure status. |
| AC-5 | Audit records exist for Tenant A and Tenant B | A Tenant A admin queries the audit log | Only Tenant A audit records are returned; Tenant B records are invisible due to PostgreSQL RLS policies. The `audit_log` table has an RLS policy: `tenant_id = current_setting('app.tenant_id')::uuid`. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL automatically capture audit records for all write operations (INSERT, UPDATE, DELETE) on business entities via EF Core `SaveChangesInterceptor`.
- FR-2: Each audit record SHALL contain: audit_log_id, tenant_id, timestamp, actor_user_id, actor_employee_no, action, resource_type, resource_id, before (JSONB), after (JSONB), ip_address, user_agent, trace_id.
- FR-3: The system SHALL audit authentication events: login success/failure, logout, password change, MFA enroll/disable, refresh-token rotation, account lockout.
- FR-4: The system SHALL audit PII reads of sensitive fields: bank account, national ID, salary details.
- FR-5: The system SHALL audit data exports (CSV, Excel, PDF) including the export parameters and row count.
- FR-6: The `audit_log` table SHALL be append-only; the application database role SHALL NOT have UPDATE or DELETE permissions on it.
- FR-7: The system SHALL enrich audit records with the HTTP request's IP address, user agent, and distributed trace ID (from Serilog/OpenTelemetry).
- FR-8: The system SHALL set `tenant_id` on every audit record from the authenticated session context.
- FR-9: The system SHALL support streaming export of audit logs to external systems (ELK / Splunk) for long-term retention.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Audit record creation overhead SHALL be <= 50 ms per save operation (P95); it must not noticeably degrade user-facing API response times.
- NFR-2: All audit data SHALL be isolated by tenant via PostgreSQL RLS policies.
- NFR-3: The `audit_log` table SHALL use BRIN indexes on the `timestamp` column for efficient time-range queries on append-only data.
- NFR-4: Audit log retention SHALL be configurable per tenant plan: 90 days (starter), 365 days (professional), 7 years (enterprise).
- NFR-5: The audit system SHALL handle high-throughput scenarios (e.g., bulk payroll run generating thousands of audit records) without blocking the business transaction.
- NFR-6: The audit_log table SHALL be partitioned by month (or by tenant range) if it exceeds a configurable size threshold (Phase 2 optimization).

## 6. Business Rules
- BR-1: Audit records are immutable; they cannot be modified or deleted through the application.
- BR-2: The `before` field is null for INSERT operations; the `after` field is null for DELETE operations.
- BR-3: Only changed fields are captured in the `before`/`after` JSONB payloads (not the entire entity).
- BR-4: Audit records for system-level actions (tenant lifecycle, impersonation) are stored in a separate `system_audit_log` table visible only to System Admins.
- BR-5: Audit log purging (per retention policy) is handled by a Hangfire scheduled job; purged records may be archived to cold storage first.
- BR-6: GDPR "right to be forgotten" anonymizes PII in audit records (replaces with `REDACTED-{id}`) while preserving the audit trail structure.

## 7. Data Requirements
**Audit record (per Technical Doc S24.3):**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| audit_log_id | bigserial (PK) | Yes | Auto-increment |
| tenant_id | uuid | Yes | RLS-enforced |
| timestamp | timestamptz | Yes | Auto-set, indexed (BRIN) |
| actor_user_id | uuid | Yes | The user who performed the action |
| actor_employee_no | varchar(50) | No | For display convenience |
| action | varchar(100) | Yes | e.g., "Employee.Update", "Leave.Approve" |
| resource_type | varchar(50) | Yes | Entity type |
| resource_id | varchar(100) | Yes | Entity ID |
| before | jsonb | No | Previous state (null for INSERT) |
| after | jsonb | No | New state (null for DELETE) |
| ip_address | varchar(50) | Yes | Client IP |
| user_agent | varchar(500) | No | Browser/client info |
| trace_id | varchar(100) | No | Distributed trace ID |

## 8. UI/UX Notes
- Audit trail is a backend/infrastructure concern with no direct user-facing creation UI.
- Audit records are consumed by the Audit Log Viewer (US-NTF-005).
- Admin notification: Tenant Admins may receive alerts for high-sensitivity audit events (e.g., bulk delete, salary changes) via the notification system.

## 9. Dependencies
- EF Core `SaveChangesInterceptor`: Core mechanism for automatic audit capture.
- PostgreSQL RLS: For tenant-isolated audit queries.
- Serilog: For enriching audit records with trace IDs and structured context.
- Hangfire: For scheduled audit log archival and purging jobs.
- Authentication module: For auditing auth events.

## 10. Assumptions & Constraints
- The EF Core interceptor approach captures all entity changes that go through `SaveChanges`/`SaveChangesAsync`; raw SQL or Dapper queries bypass this and must be audited manually.
- The database role used by the application has INSERT-only access to `audit_log`; a separate admin role is used for archival operations.
- Serilog enrichers add `tenant_id` and `trace_id` to every log entry, which are also injected into audit records.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Insert audit:** Create an employee; verify an audit record with action "Employee.Create" and `after` JSONB containing the new values.
- **Update audit:** Update an employee's department; verify `before` contains the old department_id and `after` contains the new one.
- **Delete audit:** Soft-delete a leave request; verify audit record with action "LeaveRequest.Delete".
- **Auth audit:** Log in successfully; verify audit record with action "Auth.LoginSuccess" including IP and user agent.
- **PII read audit:** Access an employee's bank details; verify audit record with action "Employee.ReadSensitive".
- **Append-only:** Attempt to UPDATE or DELETE an audit_log row using the application DB role; expect a permission denied error.
- **Tenant isolation:** Create audit records in Tenant A and B; query from Tenant A context; verify only Tenant A records returned.
- **Performance:** Run a bulk payroll for 500 employees; verify audit records are created without exceeding 50 ms overhead per save.
- **GDPR anonymization:** Execute a "right to be forgotten" for a user; verify PII fields in their audit records are replaced with "REDACTED-{id}".
