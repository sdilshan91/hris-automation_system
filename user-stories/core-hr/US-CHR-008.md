---
id: US-CHR-008
module: Core HR
priority: Should Have
persona: HR Officer / Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-008: Employee Document Management (Upload, View, Download)

## 1. Description
**As an** HR Officer or Employee,
**I want to** upload, view, and download documents associated with an employee record (contracts, ID copies, certificates, etc.),
**So that** important employee documents are centrally stored, securely accessible, and compliant with data retention policies.

## 2. Preconditions
- The user is authenticated with a valid tenant context.
- The employee record exists (see US-CHR-001).
- Object storage (Azure Blob / S3 / MinIO) is configured for the environment.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer is on an employee's profile "Documents" tab | They click "Upload Document" | A form appears to select a file (drag-and-drop or file picker), choose a document category (Contract, ID, Certificate, Other), enter an optional description, and set an optional expiry date. |
| AC-2 | The HR Officer uploads a valid file (PDF, JPEG, PNG, DOCX; <= 10 MB) | They submit | The file is stored in tenant-isolated object storage at `{tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}`, a document metadata record is created in the database with `tenant_id`, and the document appears in the employee's document list. |
| AC-3 | A user attempts to upload a file exceeding the size limit or with a disallowed MIME type | They submit | The system rejects the upload with a clear error message: "File exceeds the 10 MB limit." or "File type not allowed. Supported: PDF, JPEG, PNG, DOCX." |
| AC-4 | An authorized user clicks "Download" on a document | The system processes the request | A short-lived signed URL (expires in 5 minutes) is generated; the file downloads via the signed URL. Authorization is verified before URL generation; cross-tenant download attempts return 403. |
| AC-5 | An HR Officer sets an expiry date on a document (e.g., visa copy expiring 2027-01-15) | The document is saved | The system stores the expiry date; a background job (daily at 09:00) checks for documents expiring within 30/7/1 days and sends notifications to the HR Officer and the employee. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL support document upload with metadata: file, category, description, expiry date.
- FR-2: The system SHALL enforce file size limits (default 10 MB, configurable per plan) and MIME type whitelists.
- FR-3: The system SHALL store files in tenant-isolated object storage paths: `{tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}`.
- FR-4: The system SHALL scan uploaded files for malware (ClamAV or equivalent) before persisting the storage reference.
- FR-5: The system SHALL strip EXIF data from image uploads.
- FR-6: The system SHALL generate short-lived signed download URLs (5-minute expiry) with authorization check.
- FR-7: The system SHALL support document deletion (soft delete) by HR Officer with audit trail.
- FR-8: The system SHALL track document expiry dates and trigger notification jobs for approaching expiry.
- FR-9: The system SHALL display documents in a categorized list with file name, category, upload date, size, uploader, and expiry date.
- FR-10: Employees SHALL be able to view and download their own documents but only HR Officers can upload/delete.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: File upload SHALL complete within 5 seconds for a 10 MB file on a stable connection.
- NFR-2: All document metadata and storage paths SHALL be tenant-isolated via RLS, EF Core filters, and storage path prefixing.
- NFR-3: Cross-tenant download attempts SHALL return 403 and trigger a security alert.
- NFR-4: Storage usage SHALL count toward the tenant's plan storage quota; uploads are blocked at the threshold with an 80% warning.
- NFR-5: The document management UI SHALL be fully responsive (360px to 4K).
- NFR-6: Document access (view/download) SHALL be logged in the audit trail for compliance.

## 6. Business Rules
- BR-1: Only HR Officers can upload and delete documents on any employee's record.
- BR-2: Employees can view and download documents on their own record only.
- BR-3: Managers cannot access employee documents unless explicitly granted permission.
- BR-4: Document expiry notifications are sent at 30 days, 7 days, and 1 day before expiry.
- BR-5: Deleted documents are soft-deleted; the file in object storage is retained for the configured retention period, then hard-deleted by a background purge job.
- BR-6: The system tracks total storage usage per tenant against plan limits.
- BR-7: Supported file types: PDF, JPEG, PNG, DOCX, XLSX. Executable files (.exe, .bat, .sh, .js) are always rejected.

## 7. Data Requirements
**Employee Document table schema:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| document_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | Set from session |
| employee_id | uuid (FK) | Yes | |
| file_name | varchar(255) | Yes | Original file name |
| storage_key | varchar(500) | Yes | Object storage path/key |
| file_size_bytes | bigint | Yes | |
| mime_type | varchar(100) | Yes | |
| category | varchar(50) | Yes | Contract, ID, Certificate, Other |
| description | text | No | |
| expiry_date | date | No | |
| uploaded_by | uuid | Yes | |
| created_at / updated_at | timestamptz | Auto | |
| is_deleted | boolean | Auto | Default: false |

## 8. UI/UX Notes (Notion-like, cards-based)
- Documents section within the employee profile: card containing a categorized document list.
- Upload area: drag-and-drop zone with dashed border, file icon, and "Drop files here or click to browse" text. Smooth file drop animation.
- Each document row: file icon (based on MIME type), file name, category tag, upload date, size (human-readable), expiry badge (green if > 30 days, amber if < 30 days, red if < 7 days or expired).
- Download button (arrow-down icon) and delete button (trash icon) per row; delete requires confirmation modal.
- Category filter tabs above the document list (All, Contracts, IDs, Certificates, Other).
- Upload progress bar during file upload with percentage display.
- On mobile: document list becomes a card stack with file details stacked vertically; drag-and-drop disabled in favor of file picker button.

## 9. Dependencies
- US-CHR-001: Employee records must exist.
- US-CHR-002: Documents tab is part of the employee profile page.
- File & Document Management (Technical Doc S26): Storage infrastructure, virus scanning, signed URLs.
- Notification System (Technical Doc S25): Expiry notifications.
- Background Jobs (Technical Doc S28): Document expiry check job.

## 10. Assumptions & Constraints
- Object storage (MinIO for local dev, Azure Blob/S3 for production) is configured and accessible.
- ClamAV or equivalent antivirus is available as a service for file scanning.
- Per-tenant storage quotas are enforced based on subscription plan limits.
- Only free/open-source libraries are used.
- Files are never served directly; always via signed URLs for security.

## 11. Test Hints
- **Upload valid file:** Upload a 5 MB PDF; verify storage path includes tenant_id; verify metadata record in DB.
- **Upload invalid file:** Upload a .exe file; expect rejection. Upload a 15 MB file; expect size error.
- **Download authorization:** Download as the employee who owns the doc; expect success. Attempt download from another tenant; expect 403.
- **Expiry notification:** Set expiry date to 7 days from now; run the background job; verify notification is generated.
- **Tenant isolation:** Upload a document in Tenant A; query document list from Tenant B; verify it does not appear.
- **Storage quota:** Set tenant storage limit to 20 MB; upload files totaling 18 MB; attempt 5 MB upload; expect warning at 80% and block at limit.
- **Soft delete:** Delete a document; verify `is_deleted = true` but file remains in storage.
- **Virus scan:** Upload a test EICAR file; verify it is rejected.
