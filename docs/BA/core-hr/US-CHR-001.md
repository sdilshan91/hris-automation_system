---
id: US-CHR-001
module: Core HR
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-CHR-001: Add New Employee with Personal Information

## 1. Description
**As an** HR Officer,
**I want to** add a new employee record with their personal information, contact details, emergency contacts, and employment details,
**So that** the organization has a complete digital record from day one and downstream modules (leave, attendance, payroll) can function correctly.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant (subdomain resolved).
- At least one department exists in the tenant (see US-CHR-004).
- At least one job title exists in the tenant (see US-CHR-005).
- The tenant's subscription plan has not exceeded the maximum employee limit.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer is on the Employee module | They click "Add Employee" | A multi-step form (card-based wizard) opens with sections: Personal Info, Contact, Emergency Contact, Employment Details, and optional sections (Education, Work History, Dependents). |
| AC-2 | HR Officer fills in all mandatory fields (first name, last name, email, date of joining, department, job title, employment type) | They submit the form | A new employee record is created with status "active", a unique employee_no is auto-generated per tenant, and tenant_id is automatically set from the session context. |
| AC-3 | HR Officer enters an email that already exists for another employee in the same tenant | They attempt to submit | The system displays a validation error: "An employee with this email already exists." and prevents submission. |
| AC-4 | HR Officer uploads a profile photo | The form is submitted | The photo is stored in tenant-isolated object storage at path `{tenantId}/core-hr/{employeeId}/profile/{filename}`, with EXIF data stripped and a signed URL returned for display. |
| AC-5 | The tenant has reached its maximum employee count per subscription plan | HR Officer attempts to add a new employee | The system blocks creation and displays: "Employee limit reached for your current plan. Please upgrade or contact your administrator." |
| AC-6 | HR Officer fills custom fields configured by the Tenant Admin | They submit the form | Custom field values are persisted in the `custom_fields` JSONB column and are visible on the employee profile. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a multi-step form with the following sections: Personal Information, Contact Details, Emergency Contact, Employment Details, Education History, Work History, Dependents.
- FR-2: The system SHALL auto-generate a unique `employee_no` per tenant using a configurable pattern (e.g., "EMP-0001") with sequence isolation per tenant.
- FR-3: The system SHALL validate email uniqueness within the tenant scope only.
- FR-4: The system SHALL set `tenant_id` from the authenticated session context (never from user input).
- FR-5: The system SHALL enforce plan-level employee count limits before creating the record.
- FR-6: The system SHALL support profile photo upload with MIME type validation (JPEG, PNG, WebP), max 5 MB, EXIF stripping.
- FR-7: The system SHALL populate audit columns (`created_at`, `created_by`, `updated_at`, `updated_by`) automatically.
- FR-8: The system SHALL allow optional linking of the employee to a global user account (`user_id` FK) for self-service portal access.
- FR-9: The system SHALL render tenant-configured custom fields dynamically within the form.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Employee creation API response time SHALL be <= 800 ms (P95).
- NFR-2: All employee data SHALL be isolated by tenant via PostgreSQL RLS policies and EF Core global query filters.
- NFR-3: Profile photo upload SHALL be scanned for malware (ClamAV) before persistence.
- NFR-4: The form SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The form SHALL meet WCAG 2.1 AA accessibility standards (keyboard navigable, screen-reader friendly labels, sufficient color contrast).
- NFR-6: PII fields (phone, emergency contact) SHALL be logged in the audit trail when accessed.

## 6. Business Rules
- BR-1: `employee_no` must be unique within a tenant but can repeat across tenants.
- BR-2: `email` must be unique within a tenant but can repeat across tenants.
- BR-3: Default employee status on creation is `active` unless explicitly set to `probation`.
- BR-4: `date_of_joining` cannot be more than 90 days in the future.
- BR-5: At least one emergency contact is recommended but not mandatory on initial creation.
- BR-6: Employee records use soft delete (`is_deleted = true`); they are never hard-deleted via the UI.

## 7. Data Requirements
**Input fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| first_name | varchar(100) | Yes | Min 1, Max 100 chars |
| last_name | varchar(100) | Yes | Min 1, Max 100 chars |
| email | varchar(150) | Yes | Valid email format, unique per tenant |
| phone | varchar(20) | No | E.164 format preferred |
| date_of_birth | date | No | Must be in the past, age >= 16 |
| gender | varchar(20) | No | Enum: Male, Female, Non-Binary, Prefer Not To Say |
| date_of_joining | date | Yes | Not > 90 days in future |
| department_id | uuid | Yes | Must exist in tenant |
| job_title_id | uuid | Yes | Must exist in tenant |
| employment_type | varchar(30) | Yes | Full-Time, Part-Time, Contract, Intern |
| status | varchar(20) | No | Default: active |
| profile_photo | file | No | JPEG/PNG/WebP, max 5 MB |
| custom_fields | jsonb | No | Schema validated against tenant config |

**Output:** Created employee object with `employee_id`, `employee_no`, and all persisted fields.

## 8. UI/UX Notes (Notion-like, cards-based)
- Use a card-based multi-step wizard with a progress indicator at the top (step dots or breadcrumb trail).
- Each section (Personal, Contact, Employment, etc.) is a separate card with subtle shadow (`shadow-sm`) and rounded corners (`rounded-xl`).
- Use smooth slide or fade transitions between steps (Angular animation, 200-300ms ease-in-out).
- Form fields use Tailwind-styled Angular Material inputs with floating labels.
- Profile photo upload area: drag-and-drop zone with avatar preview, circular crop.
- "Save as Draft" and "Save & Continue" buttons at the bottom of each step.
- On mobile (< 768px): full-width single-column layout; steps become a vertical stepper.
- Success state: brief toast notification with smooth slide-in animation.
- Validation errors appear inline below fields with red accent and shake animation.

## 9. Dependencies
- US-CHR-004: Departments must exist to assign an employee to a department.
- US-CHR-005: Job titles must exist to assign a job title.
- US-CHR-012: Custom field configuration must be in place for dynamic fields to render.
- Authentication module: User must be authenticated with valid tenant context.
- File & Document Management (Technical Doc S26): For profile photo storage.

## 10. Assumptions & Constraints
- Employee creation does not automatically create a user login account; that is a separate invitation step.
- The tenant subdomain is resolved on application bootstrap and is available in the session context throughout.
- Object storage (Azure Blob / S3 / MinIO) is available and configured for the environment.
- Only free/open-source libraries are used (per project constraints).
- The system uses PostgreSQL with RLS as a defense-in-depth layer for tenant isolation.

## 11. Test Hints
- **Happy path:** Create employee with all mandatory fields; verify record in DB with correct `tenant_id` and auto-generated `employee_no`.
- **Duplicate email:** Attempt to create two employees with the same email in the same tenant; expect validation error. Verify the same email succeeds in a different tenant.
- **Plan limit:** Set tenant plan to max 5 employees, create 5, attempt 6th; expect block.
- **Tenant isolation:** Create employees in Tenant A and Tenant B; query from Tenant A context must not return Tenant B employees (verify both EF filter and RLS).
- **Photo upload:** Upload valid JPEG, oversized file (>5 MB), and disallowed MIME type (.exe); verify acceptance/rejection.
- **Custom fields:** Configure 3 custom fields via Tenant Admin, create employee with those fields populated, verify JSONB storage.
- **Responsive:** Test form at 360px, 768px, 1024px, and 1920px widths.
- **Accessibility:** Navigate the entire form using keyboard only; verify screen reader announces all labels and errors.
