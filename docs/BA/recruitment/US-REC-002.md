---
id: US-REC-002
module: Recruitment
priority: Must Have
persona: Applicant
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-002: Applicant Submits Application with Resume Upload

## 1. Description
**As an** Applicant,
**I want to** browse open vacancies, submit an application with my personal details and upload my resume,
**So that** I can be considered for a position at the organization.

## 2. Preconditions
- At least one vacancy exists with status `Open` in the tenant.
- If applying via the public careers page, the tenant has the public careers page toggle enabled.
- If applying internally (as an existing employee), the user is authenticated and has an active employee record.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A vacancy is `Open` and visible on the public careers page | An external applicant fills in the application form (name, email, phone, cover letter) and uploads a resume file | The application is saved with status `Applied`, the resume is stored in blob storage under the tenant-scoped path `{tenantId}/recruitment/{vacancyId}/{applicantId}/{filename}`, and the applicant receives a confirmation email |
| AC-2 | An applicant uploads a file | The file exceeds 25 MB or is not an allowed MIME type (PDF, DOCX, DOC) | The system rejects the upload with a clear error message and does not persist the file |
| AC-3 | An applicant has already applied to a specific vacancy | They attempt to apply again with the same email | The system prevents duplicate applications and displays a message indicating they have already applied |
| AC-4 | An internal employee is logged in | They navigate to an open internal vacancy and click "Apply" | The application is pre-filled with their employee profile data and linked to their employee record |
| AC-5 | An application is submitted to a tenant's vacancy | A user from a different tenant queries applicants | Zero applicant records from the first tenant are visible; RLS enforces tenant data isolation |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a public-facing application form with fields: first name (required), last name (required), email (required, validated), phone (optional), cover letter (optional, max 2000 chars), resume upload (required, max 25 MB, PDF/DOCX/DOC), and any tenant-configured custom fields from the application form configuration (S35.2.9).
- FR-2: The system SHALL store uploaded resumes in blob storage using tenant-scoped paths as defined in S26.2: `{tenantId}/recruitment/{vacancyId}/{applicantId}/{filename}`.
- FR-3: The system SHALL perform virus scanning (ClamAV or cloud service) on uploaded files before persisting the storage URL (S26.3).
- FR-4: The system SHALL strip EXIF data from any image files uploaded as part of the application (S26.3).
- FR-5: The system SHALL send a confirmation email to the applicant upon successful submission using the tenant's notification template for "Application Received".
- FR-6: The system SHALL create the applicant record with initial pipeline stage `Applied`.
- FR-7: The system SHALL notify users with `Recruitment.Read.All` permission (via in-app notification and optionally email) when a new application is received.
- FR-8: The system SHALL support internal applications where an authenticated employee applies; the system pre-fills profile data and links the application to the employee record.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Resume upload SHALL complete within 5 seconds for files up to 25 MB on a standard broadband connection.
- NFR-2: The public application form SHALL not require authentication and SHALL be accessible (WCAG 2.1 AA).
- NFR-3: All applicant data SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS policies.
- NFR-4: File uploads SHALL be scanned for malware before the storage URL is persisted.
- NFR-5: The application form SHALL be mobile-responsive (360px minimum width).
- NFR-6: The public careers page and application form SHALL load within 2.5 seconds (P95) on a 4G connection.

## 6. Business Rules
- BR-1: An applicant is uniquely identified per vacancy by their email address; duplicate applications to the same vacancy are rejected.
- BR-2: An applicant may apply to multiple different vacancies within the same tenant.
- BR-3: Resume file names SHALL be sanitized and renamed to a UUID-based name to prevent path traversal and collisions.
- BR-4: Only allowed MIME types (application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, application/msword) are accepted for resume uploads.
- BR-5: Internal applicants (existing employees) are flagged as `internal` on their application record.
- BR-6: Applications can only be submitted to vacancies with status `Open` and before the application deadline (if set).

## 7. Data Requirements
- **Input:** First name, last name, email, phone, cover letter text, resume file (binary), vacancy ID, custom field values, source (public/internal/referral).
- **Output:** Applicant record with UUID primary key, `tenant_id` FK, `vacancy_id` FK, pipeline stage (`Applied`), resume storage key/URL, application reference number, `applied_at` timestamp, source.
- **Storage:** `applicant` table with `tenant_id` discriminator, RLS policy. Resume stored in blob storage. Indexes on `(tenant_id, vacancy_id)`, `(tenant_id, email, vacancy_id)` for duplicate detection.

## 8. UI/UX Notes
- Public careers page: clean, tenant-branded (logo + primary color) listing of open vacancies with search and filter by department/location/employment type.
- Application form: single-page form with clear sections, inline validation, drag-and-drop file upload zone with progress indicator.
- File upload area should show the accepted formats and max size clearly.
- On successful submission, show a confirmation screen with application reference number and expected next steps.
- Mobile: full-width form fields, large touch targets for file upload, collapsible vacancy description.
- Internal application: modal or slide-over with pre-filled fields and option to update before submitting.
- Notion-like aesthetic: minimal chrome, clean typography, subtle animations on submission success.

## 9. Dependencies
- US-REC-001 (vacancy must exist and be published).
- File & Document Management module (S26) for blob storage and virus scanning.
- Notification System (S25) for confirmation emails and in-app notifications.
- Tenant module configuration for public careers page toggle and application form custom fields (S35.2.9).
- Core HR module for internal applicant profile data.

## 10. Assumptions & Constraints
- The public careers page does not require applicant account creation; applications are anonymous (identified by email).
- CAPTCHA or rate limiting should be applied to the public application form to prevent spam/bot submissions.
- Resume parsing/AI screening is out of scope for Phase 1 (S3.2).
- The confirmation email is sent via the tenant's configured SMTP/transactional email service.
- File storage uses the tenant-scoped path convention from S26.2 to ensure isolation.

## 11. Test Hints
- Upload a 25 MB PDF and verify it succeeds; upload a 26 MB file and verify rejection.
- Upload a `.exe` file renamed to `.pdf` and verify MIME type validation catches it.
- Submit two applications with the same email to the same vacancy and verify deduplication.
- Verify the resume is stored at the correct tenant-scoped blob path.
- Test cross-tenant isolation: query applicants from Tenant B and confirm Tenant A data is invisible.
- Test the confirmation email is sent and contains the correct application reference number.
- Submit an application after the vacancy deadline and verify rejection.
- Test internal application flow: verify employee data is pre-filled correctly.
- Test responsive layout on mobile (360px) -- especially the file upload zone.
