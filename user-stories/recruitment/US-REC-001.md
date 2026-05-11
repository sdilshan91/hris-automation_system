---
id: US-REC-001
module: Recruitment
priority: Must Have
persona: Recruiter
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-001: Create and Publish Job Vacancy

## 1. Description
**As a** Recruiter,
**I want to** create a job vacancy with a description, headcount, hiring manager, and required qualifications, and publish it to internal employees and optionally to a public careers page,
**So that** open positions are visible to potential candidates and the hiring pipeline can begin.

## 2. Preconditions
- The user is authenticated and has the `Recruitment.Create.All` permission within the resolved tenant.
- The tenant has the Recruitment module enabled in their subscription plan.
- At least one department and job title exist in the tenant's master data.
- The tenant's recruitment pipeline stages have been configured (or defaults are active).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A recruiter is on the Recruitment module | They fill in vacancy title, department, job title, description (rich text), headcount, hiring manager, and employment type and click "Save as Draft" | The vacancy is persisted with status `Draft` and is visible only to users with `Recruitment.Read.All` permission within the same tenant |
| AC-2 | A vacancy exists in `Draft` status | The recruiter clicks "Publish" | The vacancy status changes to `Open`, it appears on the internal vacancy listing, and if the tenant has the public careers page enabled, it also appears there |
| AC-3 | A vacancy is `Open` | The recruiter edits any field (e.g., description, headcount) and saves | The vacancy is updated, an audit log entry is created, and the updated information is reflected immediately on both internal and public listings |
| AC-4 | A recruiter in Tenant A creates a vacancy | A user in Tenant B queries vacancies | Tenant B sees zero results from Tenant A; PostgreSQL RLS policy on the `vacancy` table enforces tenant isolation |
| AC-5 | A vacancy is `Open` and has received applicants | The recruiter clicks "Close" | The vacancy status changes to `Closed`, no new applications are accepted, and existing applicants remain in their current pipeline stage |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a vacancy creation form with the following fields: title (required, max 200 chars), department (dropdown, required), job title (dropdown, required), employment type (full-time/part-time/contract/intern, required), location (dropdown), hiring manager (employee dropdown, required), headcount (integer >= 1, required), salary range (optional, min/max), description (rich text editor, required), qualifications (rich text), application deadline (optional date), and custom fields defined by the tenant.
- FR-2: The system SHALL support vacancy statuses: `Draft`, `Open`, `On Hold`, `Closed`, `Cancelled`.
- FR-3: The system SHALL allow attaching the tenant-configured pipeline stages to the vacancy (defaulting to the tenant's global pipeline if none specified).
- FR-4: The system SHALL publish the vacancy to the tenant's public careers page (if enabled in tenant module configuration -- see S35.2.9) upon status change to `Open`.
- FR-5: The system SHALL generate a unique, SEO-friendly URL slug for each published vacancy on the public careers page.
- FR-6: The system SHALL allow bulk status changes (e.g., close multiple vacancies at once).
- FR-7: The system SHALL log all vacancy create/update/publish/close actions to the tenant's audit trail.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Vacancy list page SHALL load within 400 ms (P95) for up to 500 active vacancies per tenant.
- NFR-2: All vacancy data SHALL be tenant-scoped via `tenant_id` column with PostgreSQL RLS policies enforced as defense-in-depth.
- NFR-3: The vacancy creation form SHALL be fully responsive from 360px to 4K resolution.
- NFR-4: Rich text editor SHALL sanitize HTML input to prevent XSS (OWASP Top 10 control).
- NFR-5: The public careers page SHALL be accessible without authentication and comply with WCAG 2.1 AA.

## 6. Business Rules
- BR-1: Only users with `Recruitment.Create.All` or `Recruitment.Manage.All` permission can create or edit vacancies.
- BR-2: A vacancy cannot be published without at least a title, department, job title, hiring manager, headcount, and description.
- BR-3: Closing a vacancy does not delete or reject existing applicants -- they remain in their current stage until explicitly moved.
- BR-4: A vacancy's headcount represents the maximum number of positions to fill; the system SHALL track filled count as applicants are converted to employees.
- BR-5: The public careers page toggle is a tenant-level configuration; individual vacancies inherit this setting but can be individually excluded from the public page.

## 7. Data Requirements
- **Input:** Vacancy title, department ID, job title ID, employment type, location ID, hiring manager (employee ID), headcount, salary range (min/max, currency), description (HTML), qualifications (HTML), application deadline, custom field values.
- **Output:** Vacancy record with UUID primary key, `tenant_id` foreign key, auto-generated vacancy reference number (e.g., `VAC-2026-0001`), status, created/updated timestamps, created_by user ID.
- **Storage:** `vacancy` table with `tenant_id` discriminator, RLS policy, and indexes on `(tenant_id, status)` and `(tenant_id, department_id)`.

## 8. UI/UX Notes
- Follow the Notion-like design aesthetic: clean whitespace, subtle shadows, smooth transitions.
- Vacancy creation should use a slide-over panel or full-page form with clear section grouping (Basic Info, Description, Requirements, Settings).
- Rich text editor for description and qualifications fields (use a free open-source library such as TipTap or ngx-editor).
- Vacancy list view should support both table view and card/grid view with status badges (color-coded: Draft=gray, Open=green, On Hold=amber, Closed=red).
- Inline status change via dropdown on the list view for quick actions.
- Hiring manager field should be a searchable employee dropdown with avatar preview.
- Mobile: stack form fields vertically; collapse rich text toolbar to essential actions.

## 9. Dependencies
- Core HR module (departments, job titles, employees for hiring manager selection).
- Tenant module configuration for public careers page toggle (S35.2.9).
- Audit logging module for tracking vacancy changes.
- File & Document Management for any attachments to the vacancy (optional JD document upload).

## 10. Assumptions & Constraints
- Pipeline stages are pre-configured at the tenant level before vacancies are created (default stages: Applied, Screening, Interview, Offer, Hired, Rejected).
- The public careers page is a simple, tenant-branded listing -- not a full-featured career site builder (Phase 1).
- Vacancy reference numbers are sequential per tenant and year, auto-generated by the system.
- All vacancy data is stored in the shared PostgreSQL database with `tenant_id` discriminator.

## 11. Test Hints
- Verify RLS: create vacancies in two tenants, confirm cross-tenant queries return empty results.
- Test status transitions: Draft -> Open -> On Hold -> Open -> Closed; verify invalid transitions are rejected.
- Test that publishing a vacancy with the public careers page disabled does not expose it publicly.
- Validate rich text sanitization: inject `<script>` tags in the description and verify they are stripped.
- Test form validation: submit without required fields and verify appropriate error messages.
- Test responsive layout at 360px, 768px, 1024px, and 1920px breakpoints.
- Performance: load-test vacancy list with 500 vacancies and verify P95 < 400 ms.
