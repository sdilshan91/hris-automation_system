---
id: US-REC-010
module: Recruitment
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-010: Convert Accepted Applicant to Employee Record

## 1. Description
**As an** HR Officer,
**I want to** convert an applicant who has accepted an offer into a full employee record, pre-filling data from the application and offer,
**So that** the onboarding process can begin immediately without redundant data entry.

## 2. Preconditions
- The user is authenticated and has `Recruitment.Manage.All` and `Employee.Create.All` permissions within the resolved tenant.
- The applicant is in the "Hired" pipeline stage with an offer in `Accepted` status.
- The tenant has not exceeded its subscription plan's maximum employee limit.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An applicant has accepted an offer and is in the "Hired" stage | The HR Officer clicks "Convert to Employee" on the applicant's detail panel | A pre-filled employee creation form opens with data mapped from the application (name, email, phone) and the offer (job title, department, salary, start date, reporting manager) |
| AC-2 | The HR Officer reviews and completes the pre-filled form | They fill in remaining required fields (employee number, employment type, work location) and click "Create Employee" | A new employee record is created in the Core HR module, the applicant record is linked to the employee record, and the vacancy's filled count is incremented |
| AC-3 | The employee record is created | The system processes the conversion | A user account is optionally created (if configured) with the employee's email, assigned the default "Employee" role within the tenant, and a welcome/onboarding email is sent |
| AC-4 | The conversion is completed | The HR Officer views the applicant record | The applicant record shows a "Converted" badge with a link to the newly created employee record; the vacancy shows the updated filled/headcount ratio |
| AC-5 | A conversion is performed in Tenant A | A user in Tenant B queries employees | The newly created employee is only visible in Tenant A; RLS policies enforce tenant isolation on the `employee` table |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a "Convert to Employee" action on the applicant detail panel, available only when the applicant is in the "Hired" stage with an accepted offer.
- FR-2: The system SHALL pre-fill the employee creation form with mapped data: first name, last name, email, phone (from application); job title, department, reporting manager, salary, start date (date of joining), probation period (from offer).
- FR-3: The system SHALL allow the HR Officer to review, modify, and complete the pre-filled data before creating the employee record.
- FR-4: The system SHALL auto-generate the employee number following the tenant's configured numbering pattern (e.g., `EMP-2026-0042`).
- FR-5: The system SHALL create a linked `User` and `UserTenant` record for the new employee (if automatic account creation is enabled in tenant settings), assigning the default "Employee" role.
- FR-6: The system SHALL update the applicant record with: `converted_to_employee_id` (FK to the new employee), `converted_at` timestamp, and `converted_by` user ID.
- FR-7: The system SHALL increment the vacancy's `filled_count` and, if `filled_count` equals `headcount`, automatically change the vacancy status to `Closed` with a notification to the recruiter.
- FR-8: The system SHALL trigger the onboarding workflow (if configured) for the new employee, including checklist generation per the employee's role/department.
- FR-9: The system SHALL send a welcome email to the new employee with login credentials (if user account was created) and onboarding instructions.
- FR-10: The system SHALL prevent duplicate conversions: an applicant who has already been converted cannot be converted again.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The conversion process (applicant record update + employee creation + user account creation + vacancy update) SHALL complete within 2 seconds (P95) as an atomic transaction.
- NFR-2: All data created during conversion SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: The conversion transaction SHALL be atomic: if any step fails (employee creation, user account creation, vacancy update), the entire operation is rolled back.
- NFR-4: The pre-filled form SHALL load within 400 ms (P95).
- NFR-5: Welcome emails SHALL be sent asynchronously via Hangfire, not blocking the conversion API response.

## 6. Business Rules
- BR-1: Conversion requires both `Recruitment.Manage.All` and `Employee.Create.All` permissions.
- BR-2: An applicant can only be converted once; the system SHALL reject duplicate conversion attempts with a clear error message.
- BR-3: If the tenant's employee count would exceed the subscription plan's `MaxEmployees` limit after conversion, the system SHALL block the conversion and display a message to upgrade the plan.
- BR-4: The employee's `date_of_joining` defaults to the offer's `start_date` but can be overridden by the HR Officer.
- BR-5: If the vacancy's headcount is fully filled after conversion, the vacancy auto-closes and remaining applicants in the pipeline are notified (if configured).
- BR-6: The conversion creates a link between the applicant and employee records for traceability; the applicant record is not deleted.
- BR-7: User account creation is optional and controlled by a tenant setting ("Auto-create user accounts on hire"). If disabled, the HR Officer must create the account separately.

## 7. Data Requirements
- **Input:** Applicant ID (pre-selected), pre-filled employee data (editable), additional required employee fields (employee number, employment type, work location, bank details for payroll).
- **Output:** New `employee` record with UUID, `tenant_id`; updated `applicant` record with `converted_to_employee_id`; new `user` and `user_tenant` records (if auto-create enabled); updated `vacancy` with incremented `filled_count`.
- **Data Mapping:**
  - `applicant.first_name` -> `employee.first_name`
  - `applicant.last_name` -> `employee.last_name`
  - `applicant.email` -> `employee.email` + `user.email`
  - `applicant.phone` -> `employee.phone`
  - `offer.offered_position` -> `employee.job_title_id`
  - `offer.department_id` -> `employee.department_id`
  - `offer.offered_salary` -> employee salary structure
  - `offer.start_date` -> `employee.date_of_joining`
  - `offer.reporting_manager_id` -> `employee.reporting_manager_id`

## 8. UI/UX Notes
- "Convert to Employee" button: prominent action button on the applicant detail panel, only visible for "Hired" applicants with accepted offers. Styled as a primary action (green or brand color).
- Pre-filled form: two-column layout on desktop, single column on mobile. Pre-filled fields are visually distinct (subtle highlight or "auto-filled" label). All fields are editable.
- Employee number: auto-generated with a "lock" icon; click to override manually.
- Conversion confirmation: summary dialog showing key details (name, position, department, start date, salary) before final creation.
- Success state: animated success message with links to "View Employee Profile" and "Start Onboarding Checklist".
- Notion-like aesthetic: clean form layout, section headers (Personal Info, Employment Details, Compensation), smooth transitions.
- Mobile: full-width form, sticky "Create Employee" button at the bottom.

## 9. Dependencies
- US-REC-004 (applicant must be in "Hired" stage).
- US-REC-007 (offer must be in "Accepted" status for data mapping).
- Core HR module (employee creation, department, job title entities).
- Authentication module (user + user_tenant + role creation).
- Onboarding module (trigger onboarding checklist -- US for onboarding module).
- Notification System (S25) for welcome email.
- Hangfire (S28) for async email delivery.
- Subscription plan enforcement (tenant employee limit check).

## 10. Assumptions & Constraints
- The conversion is a one-way operation; there is no "un-convert" function. If a hire falls through, the employee record must be separately deactivated/terminated.
- Bank details and other payroll-specific fields are not available from the application; they must be entered manually or collected during onboarding.
- The resume and application documents remain linked to the applicant record; they are not copied to the employee record but are accessible via the applicant-employee link.
- If the tenant has onboarding checklists configured, the conversion triggers checklist generation but does not block the conversion if the onboarding module is disabled.
- The user account password is either set via a "Set Password" email link (invite flow) or the employee uses social login.

## 11. Test Hints
- Convert an applicant and verify the employee record is created with all mapped fields correct.
- Verify the applicant record has `converted_to_employee_id` set and shows a "Converted" badge.
- Verify the vacancy `filled_count` is incremented; if fully filled, verify the vacancy auto-closes.
- Attempt to convert the same applicant again and verify the system rejects it.
- Test subscription limit: set `MaxEmployees` to the current employee count, attempt conversion, and verify it is blocked with an upgrade message.
- Test atomic transaction: simulate a failure during user account creation (e.g., duplicate email) and verify the entire conversion is rolled back (no orphan employee record).
- Test user account creation: verify a `user`, `user_tenant`, and `user_tenant_role` (Employee role) are created.
- Verify the welcome email is queued via Hangfire and delivered with correct content.
- Test cross-tenant isolation: verify the new employee is only visible in the correct tenant.
- Test onboarding trigger: verify the onboarding checklist is generated for the new employee (if onboarding module is enabled).
