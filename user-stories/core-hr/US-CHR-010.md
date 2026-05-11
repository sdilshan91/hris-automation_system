---
id: US-CHR-010
module: Core HR
priority: Should Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-010: Bulk Employee Import via CSV/Excel

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** import multiple employee records at once by uploading a CSV or Excel file,
**So that** I can onboard large numbers of employees efficiently without manually creating each record one by one.

## 2. Preconditions
- The user is authenticated with HR Officer or Tenant Admin role within their tenant.
- Departments and job titles referenced in the import file exist in the tenant (see US-CHR-004, US-CHR-005).
- The tenant's subscription plan has sufficient employee capacity for the imported records.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer navigates to the bulk import page | The page loads | They see a download link for a template file (CSV and Excel formats) with column headers matching the expected import schema, sample data rows, and a column description guide. |
| AC-2 | The HR Officer uploads a valid CSV/Excel file with 50 employee records | They click "Import" | The system validates all rows, creates 50 employee records with `tenant_id` set from session context and auto-generated `employee_no` values, and displays a summary: "50 of 50 records imported successfully." |
| AC-3 | The uploaded file contains 100 rows, 5 of which have validation errors (missing required fields, duplicate emails, invalid department names) | The system processes the file | The system imports the 95 valid rows and returns a detailed error report for the 5 failed rows, listing the row number, field name, and error description. The user can download the error report as a file. |
| AC-4 | The import file contains 10,000 rows | The HR Officer uploads it | The system accepts the file, queues the import as an asynchronous background job (Hangfire), displays a progress indicator, and notifies the user (in-app + email) when the import completes with a summary report. |
| AC-5 | The import would exceed the tenant's employee limit (e.g., plan allows 100, currently 80, file has 30 rows) | They attempt to import | The system pre-validates the count and warns: "This import would exceed your plan's employee limit (100). Only 20 of 30 records can be imported. Upgrade your plan or reduce the file." The user can choose to import the first 20 or cancel. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL accept CSV (.csv) and Excel (.xlsx) file uploads for bulk import.
- FR-2: The system SHALL provide a downloadable import template with headers, sample data, and field descriptions.
- FR-3: The system SHALL validate each row against the employee schema: required fields, data types, email format, email uniqueness (within tenant), department existence (by name or ID), job title existence (by name or ID).
- FR-4: The system SHALL support partial import: valid rows are imported, invalid rows are skipped and reported.
- FR-5: The system SHALL auto-generate `employee_no` for each imported record per the tenant's numbering pattern.
- FR-6: The system SHALL set `tenant_id` from session context for all imported records (never from the file).
- FR-7: For files with more than 500 rows, the system SHALL process the import asynchronously as a Hangfire background job with tenant context.
- FR-8: The system SHALL provide a downloadable error report (CSV) listing row number, field, and error for each failed row.
- FR-9: The system SHALL enforce plan-level employee count limits before and during import.
- FR-10: The system SHALL log the import operation in the audit trail with file name, row count, success count, and failure count.
- FR-11: The system SHALL support mapping custom fields in the import file to the tenant's configured custom fields.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Bulk import of 10,000 rows SHALL complete within 5 minutes (async processing).
- NFR-2: All imported records SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: The import process SHALL be idempotent: re-uploading the same file does not create duplicate records (email uniqueness check prevents this).
- NFR-4: The import SHALL use database transactions: for synchronous imports (<= 500 rows), all-or-nothing per batch; for async, batch commits of 100 rows with rollback per batch on error.
- NFR-5: The import UI SHALL be fully responsive (360px to 4K).
- NFR-6: Memory usage SHALL be bounded: the system SHALL stream/chunk-read large files rather than loading entirely into memory.

## 6. Business Rules
- BR-1: `tenant_id` is always set from the session context, not from the import file.
- BR-2: Duplicate emails within the file itself are flagged as errors.
- BR-3: If a department or job title name in the file does not match any existing record in the tenant, the row fails validation.
- BR-4: Imported employees default to `active` status unless a status column is provided.
- BR-5: The import does not create user accounts; employees must be separately invited to the portal.
- BR-6: Excel parsing uses ClosedXML (free, open-source); CSV parsing uses CsvHelper or equivalent.
- BR-7: Maximum file size: 25 MB.

## 7. Data Requirements
**Import template columns:**
| Column | Required | Validation |
|--------|----------|------------|
| first_name | Yes | Max 100 chars |
| last_name | Yes | Max 100 chars |
| email | Yes | Valid email, unique per tenant |
| phone | No | E.164 format |
| date_of_birth | No | Date, past |
| gender | No | Male/Female/Non-Binary/Prefer Not To Say |
| date_of_joining | Yes | Date |
| department_name | Yes | Must exist in tenant |
| job_title_name | Yes | Must exist in tenant |
| employment_type | Yes | Full-Time/Part-Time/Contract/Intern |
| location_name | No | Must exist in tenant if provided |
| status | No | Default: active |
| custom_field_* | No | Per tenant config |

**Output:** Import summary report with total rows, success count, failure count, and error details.

## 8. UI/UX Notes (Notion-like, cards-based)
- Import page layout: clean card with 3-step process (Download Template -> Upload File -> Review Results).
- Step 1 card: Download template links (CSV, Excel) with file format guide collapsible section.
- Step 2 card: Drag-and-drop upload zone with file picker fallback. Show file name and size after selection. "Import" button.
- Step 3 card (results): Summary banner (green if all success, amber if partial, red if all failed) with counts. Error table below listing row number, field, and error with option to download as CSV.
- For async imports: progress bar with percentage, estimated time remaining, and a note: "You'll be notified when the import completes. You can navigate away from this page."
- On mobile: steps stack vertically; drag-and-drop replaced by file picker button; error table scrolls horizontally.
- Smooth transitions between steps (slide animation, 300ms).

## 9. Dependencies
- US-CHR-001: Employee creation logic is reused for each imported row.
- US-CHR-004: Department names must be resolvable within the tenant.
- US-CHR-005: Job title names must be resolvable within the tenant.
- US-CHR-007: Location names must be resolvable if provided.
- US-CHR-012: Custom fields must be configured for dynamic columns.
- Background Jobs (Technical Doc S28): Hangfire for async processing.
- Notification System (Technical Doc S25): Completion notification for async imports.

## 10. Assumptions & Constraints
- ClosedXML is used for Excel parsing (free, open-source, MIT license).
- CsvHelper or equivalent is used for CSV parsing.
- The import does not update existing records; it only creates new ones. An "update import" feature may be added in Phase 2.
- Large file processing streams rows to avoid excessive memory usage.
- Only free/open-source libraries are used.

## 11. Test Hints
- **Happy path:** Upload a CSV with 10 valid rows; verify all 10 employees created with correct `tenant_id` and `employee_no`.
- **Partial failure:** Upload a file with 8 valid and 2 invalid rows; verify 8 created, 2 in error report with row numbers.
- **Duplicate email in file:** Include 2 rows with the same email; verify the second is flagged as duplicate.
- **Non-existent department:** Include a row with "Marketing" when no such department exists; verify error.
- **Plan limit:** Set plan limit to 50, current count 48, upload 5 rows; verify pre-validation warning about exceeding limit.
- **Large file (async):** Upload a file with 1000 rows; verify it's queued as a background job and user is notified on completion.
- **Tenant isolation:** Import employees in Tenant A; verify they are not visible from Tenant B.
- **Template download:** Download CSV and Excel templates; verify headers match the expected schema.
- **Memory:** Upload a 10,000-row file; monitor server memory; verify no excessive allocation.
