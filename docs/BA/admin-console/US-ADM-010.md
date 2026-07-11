---
id: US-ADM-010
module: Admin Console — System Admin / Tenant Admin
priority: Must Have
persona: System Admin (Platform Operator Staff), Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ADM-010: Tenant Data Export on Demand

## 1. Description
**As a** System Admin or Tenant Admin,
**I want to** initiate a full or partial data export of a tenant's data — including all business entities as CSV, uploaded documents as a zip archive, audit logs as JSON Lines, and a manifest with row counts and checksums,
**So that** tenants can exercise their right to data portability (GDPR Article 20), perform backups, migrate to another system, or extract their data during the termination grace period.

## 2. Preconditions
- The requesting user is authenticated as either:
  - A System Admin at `admin.yourhrm.com` (can export any tenant's data), or
  - A Tenant Admin at `{subdomain}.yourhrm.com` (can export only their own tenant's data).
- The tenant is in `active`, `trial`, `past_due`, or `terminating` status (not `suspended` or `terminated`).
- Sufficient storage space is available for the export bundle generation.
- The Hangfire background job system is operational.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The Tenant Admin navigates to Data Management > Export or the System Admin selects "Export Data" for a tenant | They select the export scope (full export or select specific entities: Employees, Leave, Attendance, Payroll, etc.) and click "Start Export" | A Hangfire background job is enqueued to generate the export bundle. The user sees a confirmation: "Export started. You will receive an email with the download link when it is ready." The export request is logged in the audit log. |
| AC-2 | The export Hangfire job completes | The job finishes processing | The export bundle is generated containing: one CSV file per entity (e.g., `employees.csv`, `leave_requests.csv`, `payroll_runs.csv`), a ZIP archive of all uploaded documents (resumes, payslips, profile photos) organized by entity, audit logs as a JSON Lines file (`audit_log.jsonl`), a schema documentation file (PDF describing column definitions), and a `manifest.json` with entity names, row counts, file sizes, and SHA-256 checksums. The bundle is stored in tenant-scoped storage (`{tenantId}/exports/{export_id}/`). A download URL (time-limited, 72-hour expiry) is emailed to the requesting user and the tenant's billing contact. |
| AC-3 | The Tenant Admin clicks the download link | They access the URL within 72 hours | The export bundle is downloaded as a single ZIP file. After 72 hours, the link expires and the file is automatically deleted from storage. The download event is logged in the audit log. |
| AC-4 | A Tenant Admin in a `terminating` tenant requests an export | They initiate the export during the grace period | The export is allowed (export endpoints remain active during the `terminating` state). The export includes all data that would be deleted when the grace period expires, giving the tenant a complete copy before deletion. |
| AC-5 | A Tenant Admin attempts to export another tenant's data | They manipulate API parameters to target a different `tenant_id` | The request is rejected. The `ITenantContext` ensures the export is scoped to the authenticated user's tenant only. PostgreSQL RLS prevents any cross-tenant data access. The attempt is logged as a security event. |
| AC-6 | The System Admin exports data for a tenant | They initiate the export from the System Admin console | The export bundle includes the same content as a tenant-initiated export. The action is logged in both `system_audit_log` and the tenant's `audit_log` with the System Admin as the actor. The download link is sent to the System Admin's email and the tenant's billing contact. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The export initiation endpoint shall accept: `tenant_id` (implicit from context for Tenant Admin, explicit for System Admin), `scope` (enum: `full` or array of entity codes), and `format` preferences (CSV delimiter, date format).
- FR-2: The export job shall query each selected entity table filtered by `tenant_id`, serialize to CSV with headers, and package into the export bundle.
- FR-3: The CSV files shall use UTF-8 encoding with BOM, comma delimiter (configurable), and include column headers matching the entity field names.
- FR-4: Uploaded documents shall be collected from the tenant-scoped storage path (`{tenantId}/documents/`) and included in a `documents/` subdirectory within the export ZIP, organized by entity type (e.g., `documents/employees/{employee_id}/`, `documents/payslips/{payslip_id}/`).
- FR-5: The audit log export shall be in JSON Lines format, one record per line, with all fields including `before`/`after` JSON.
- FR-6: The `manifest.json` shall include: `export_id`, `tenant_id`, `tenant_name`, `export_timestamp`, `scope`, and for each file: `filename`, `entity`, `row_count`, `file_size_bytes`, `sha256_checksum`.
- FR-7: The download URL shall be a signed URL (pre-signed S3 URL or equivalent) with a 72-hour expiry. After expiry, the export files are deleted by a cleanup Hangfire job.
- FR-8: Sensitive fields (password hashes, MFA secrets, token hashes) shall be excluded from the export. PII fields (national ID, bank account) shall be included but clearly marked in the schema documentation.
- FR-9: Only one export job per tenant shall be allowed at a time. If an export is already in progress, the UI shall show the current export's status with an estimated completion time.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The export job shall complete within 30 minutes for a tenant with up to 50,000 employee records and 10 GB of documents.
- NFR-2: The export job shall not significantly impact production database performance; use `AsNoTracking()`, read replicas (if available), and streaming serialization to avoid loading all data into memory.
- NFR-3: The export bundle shall be encrypted at rest in storage and transmitted over HTTPS.
- NFR-4: All export events (initiation, completion, download, expiry) shall be audited.
- NFR-5: The export functionality shall be accessible on mobile devices (responsive UI for initiating the export and viewing status).

## 6. Business Rules
- BR-1: Tenant Admins can export only their own tenant's data. System Admins can export any tenant's data.
- BR-2: Export is not available for tenants in `suspended` status (they must contact support to request data; System Admin can initiate on their behalf).
- BR-3: Export is explicitly available during the `terminating` grace period (this is a primary use case — data extraction before deletion).
- BR-4: The export does not include system-level data (subscription plan details, billing records) — only tenant-scoped business data.
- BR-5: Export is rate-limited to one concurrent export per tenant and a maximum of 3 exports per tenant per calendar month (to prevent abuse).
- BR-6: The export download link is sent to both the requesting user and the tenant's billing contact for transparency.
- BR-7: Sensitive authentication fields (password hashes, MFA secrets, refresh token hashes) are never included in exports.

## 7. Data Requirements
- **Input:** `scope` (full or entity list), `format_options` (delimiter, date format).
- **Output:** ZIP archive containing CSV files, document archive, audit log JSONL, schema PDF, and manifest JSON.
- **Entities included in full export:** Employees, Departments, Job Titles, Locations, Leave Types, Leave Requests, Leave Balances, Holiday Calendars, Attendance Records, Shifts, Payroll Runs, Payslips, Salary Structures, Salary Components, Recruitment Vacancies, Applicants, Interviews, Offers, Performance Goals, Appraisals, Training Courses, Benefits, Asset Records, Custom Fields, Workflow Definitions, Users (name, email, roles — no credentials), Audit Log.
- **Storage:** `{tenantId}/exports/{export_id}/export_bundle.zip`.
- **Tables affected (for audit):** `audit_log`, `system_audit_log`.

## 8. UI/UX Notes
- Export page (Tenant Admin): a clean interface showing export scope options (full export toggle, or entity checkboxes grouped by module), format preferences, and a "Start Export" button.
- Export history: a list of recent exports showing: date, scope, status (queued/processing/completed/expired), file size, and download link (if still valid).
- Progress indicator: while an export is in progress, show a progress bar or status indicator (queued -> processing entities -> packaging -> complete).
- System Admin: the export action is accessible from the tenant detail view as a button in the "Actions" section.
- Notion-like aesthetic: clean checkboxes for entity selection, grouped under module headers with subtle section dividers, smooth progress animations.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned with data to export.
- US-ADM-004: Export must work during `terminating` state.
- Hangfire for background job processing.
- File storage service for export bundle storage.
- Email service for download link delivery.
- Audit logging infrastructure.

## 10. Assumptions & Constraints
- Phase 1: Export format is CSV + ZIP. Other formats (Excel, SQL dump) may be added in Phase 2.
- The schema documentation PDF is a static document generated at build time, not dynamically per export. It describes the standard entity schema.
- For very large tenants (> 100,000 employees), the export may need to be streamed in chunks. Phase 1 targets up to 50,000 employees.
- The export does not include the tenant's configuration/settings (branding, workflows, etc.) in a re-importable format. This is a data export, not a full tenant backup.
- Document export size is limited by the tenant's storage usage (capped by plan `max_storage_gb`).

## 11. Test Hints
- **Full export:** Populate a tenant with employees, leave records, attendance, and payslips. Initiate a full export. Verify the ZIP contains CSV files for each entity, documents, audit log JSONL, schema PDF, and manifest with correct row counts and checksums.
- **Partial export:** Select only Employees and Leave; verify only those CSVs are in the export.
- **Manifest validation:** Verify SHA-256 checksums in the manifest match the actual file checksums.
- **Cross-tenant isolation:** As Tenant A admin, initiate export with Tenant B's `tenant_id` in the request; verify rejection. Verify the export ZIP contains zero records from Tenant B.
- **Terminating tenant export:** Set a tenant to `terminating` status; initiate export; verify it succeeds.
- **Suspended tenant export:** Set a tenant to `suspended`; attempt export as Tenant Admin; verify rejection. Attempt as System Admin; verify success.
- **Download link expiry:** Generate an export, advance time past 72 hours; attempt download; verify the link is expired and the file is deleted.
- **Sensitive field exclusion:** Verify that password hashes, MFA secrets, and token hashes are not present in any CSV file.
- **Rate limiting:** Initiate 3 exports in the same month; attempt a 4th; verify rejection with "Monthly export limit reached."
- **Concurrent export prevention:** Initiate an export; while it is processing, attempt a second; verify the second is queued or rejected with a message about the in-progress export.
- **Audit trail:** Verify that export initiation, completion, and download events are all recorded in the audit log.
