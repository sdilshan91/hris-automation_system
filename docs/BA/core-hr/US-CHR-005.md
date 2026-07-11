---
id: US-CHR-005
module: Core HR
priority: Must Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-005: Create and Manage Job Titles and Positions

## 1. Description
**As a** Tenant Admin or HR Officer,
**I want to** create, view, edit, and deactivate job titles and optionally link them to salary grades,
**So that** employees can be assigned standardized titles and the organization's position structure is consistent and reportable.

## 2. Preconditions
- The user is authenticated with Tenant Admin or HR Officer role within their tenant.
- Tenant context is resolved from the subdomain.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin navigates to the Job Titles management page | The page loads | A list/table of existing job titles is displayed with columns: Title Name, Grade (if linked), Employee Count, Status, and action buttons. |
| AC-2 | The admin clicks "Add Job Title" and fills in a unique title name | They submit the form | A new job_title record is created with `tenant_id` from session context and `title_name` unique within the tenant. |
| AC-3 | The admin enters a title name that already exists in the tenant | They submit | The system rejects with: "A job title with this name already exists." |
| AC-4 | The admin links a job title to a salary grade | They save | The `grade_id` FK is set; when this job title is assigned to an employee, the associated grade is displayed on the employee profile. |
| AC-5 | The admin attempts to deactivate a job title assigned to active employees | They click "Deactivate" | The system warns: "This job title is assigned to X active employees. Reassign them before deactivating." and blocks the action. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL support CRUD operations on job titles scoped to the current tenant.
- FR-2: The system SHALL enforce unique `title_name` within a tenant.
- FR-3: The system SHALL optionally link a job title to a salary grade (`grade_id` FK, nullable).
- FR-4: The system SHALL display the count of employees assigned to each job title.
- FR-5: The system SHALL use soft delete for job titles; deactivated titles are hidden from assignment dropdowns but visible in admin views.
- FR-6: The system SHALL support employment types (Full-Time, Part-Time, Contract, Intern) as a separate reference entity usable alongside job titles.
- FR-7: The system SHALL prevent deactivation of job titles with active employee assignments.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Job title CRUD API response time SHALL be <= 400 ms for reads, <= 800 ms for writes (P95).
- NFR-2: All job title data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: The management page SHALL be fully responsive (360px to 4K).
- NFR-4: Audit log entries SHALL be created for all job title create, update, and deactivate operations.

## 6. Business Rules
- BR-1: Job title names are unique within a tenant but can repeat across tenants.
- BR-2: A job title can exist without a linked grade.
- BR-3: Deactivated job titles cannot be assigned to new employees but remain visible on existing employee records.
- BR-4: Job titles are tenant-specific master data; there are no system-wide predefined titles.
- BR-5: Grades, if used, are also tenant-specific entities.

## 7. Data Requirements
**Job Title table schema:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| job_title_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | Set from session |
| title_name | varchar(150) | Yes | Unique per tenant |
| grade_id | uuid (FK) | No | Nullable, links to grade entity |
| description | text | No | |
| is_active | boolean | Yes | Default: true |
| created_at / updated_at | timestamptz | Auto | |
| created_by / updated_by | uuid | Auto | |
| is_deleted | boolean | Auto | Default: false |

## 8. UI/UX Notes (Notion-like, cards-based)
- Job titles page: clean card-based table with search bar at the top.
- "Add Job Title" button (top-right) opens a compact modal or slide-over panel.
- Form fields: Title Name (text input), Grade (searchable dropdown, optional), Description (textarea), Status toggle.
- Each row shows the employee count as a clickable badge that navigates to the directory filtered by that job title.
- Inline status toggle (active/inactive) with confirmation for deactivation.
- Row actions: Edit, Deactivate, with hover-revealed action icons.
- On mobile: table becomes a card list with stacked fields.
- Subtle shadow and rounded corners on the table card container (`rounded-xl shadow-sm`).

## 9. Dependencies
- US-CHR-001: Employees reference job titles via `job_title_id` FK.
- US-CHR-003: Employee directory filters by job title.
- Payroll module (future): Salary grades linked to job titles feed into compensation structures.

## 10. Assumptions & Constraints
- Salary grades are a separate entity that may be implemented alongside or after job titles; the `grade_id` FK is nullable to allow phased delivery.
- Employment types (Full-Time, Part-Time, etc.) are stored as a reference/enum rather than a full entity in Phase 1.
- Only free/open-source libraries are used.

## 11. Test Hints
- **Create job title:** Create a new title; verify it appears in the list and is available in employee assignment dropdowns.
- **Duplicate name:** Attempt to create a duplicate title in the same tenant; expect error. Verify different tenant accepts the same name.
- **Grade linking:** Create a title with a grade; assign to employee; verify grade appears on employee profile.
- **Deactivate with employees:** Assign a title to an employee; attempt deactivation; expect warning/block.
- **Tenant isolation:** Create titles in Tenant A; query from Tenant B; verify zero results.
- **Employee count:** Assign 3 employees to a title; verify the count badge shows 3.
- **Audit trail:** Create and edit a title; verify audit_log entries.
