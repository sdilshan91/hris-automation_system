---
id: TC-CHR-065
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-065: Create employee with all mandatory fields -- happy path (AC-2)

## 1. Test Objective
Verify that an HR Officer can create a new employee by filling all mandatory fields, resulting in a record with status "active", an auto-generated unique employee_no per tenant, tenant_id automatically set from the session context, and audit columns populated.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4, FR-7
- Business Rules: BR-1, BR-3
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department "Engineering" exists in the "acme" tenant.
- Job title "Software Engineer" exists in the "acme" tenant.
- No existing employee with email "john.doe@example.com" in the "acme" tenant.
- Tenant subscription plan allows at least one more employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| first_name | John | Required, varchar(100) |
| last_name | Doe | Required, varchar(100) |
| email | john.doe@example.com | Required, unique per tenant |
| date_of_joining | 2026-06-15 | Required, within 90-day future window |
| department_id | (Engineering UUID) | Required, must exist in tenant |
| job_title_id | (Software Engineer UUID) | Required, must exist in tenant |
| employment_type | Full-Time | Required |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module and click "Add Employee" | Multi-step wizard opens. |
| 2 | Fill in Personal Info: first_name = "John", last_name = "Doe", email = "john.doe@example.com" | Fields accept values with no validation errors. |
| 3 | Navigate to Employment Details step | Employment Details card loads. |
| 4 | Fill in date_of_joining = "2026-06-15", department = "Engineering", job_title = "Software Engineer", employment_type = "Full-Time" | Fields accept values. |
| 5 | Submit the form (click final Save/Submit) | API call `POST /api/v1/tenant/employees` is made. Response is 201 Created. |
| 6 | Verify the response contains a new employee_id (UUID) | employee_id is present and non-null. |
| 7 | Verify employee_no is auto-generated (e.g., "EMP-0001") | employee_no matches the configured tenant pattern and is unique. |
| 8 | Verify status is "active" | Default status is active per BR-3. |
| 9 | Verify tenant_id in the database matches the "acme" tenant's ID | tenant_id is automatically stamped from session, not from user input. |
| 10 | Verify audit columns: created_at is set to current timestamp, created_by is set to the authenticated user | FR-7: Audit columns populated automatically. |
| 11 | Verify a success toast notification is displayed | Brief toast with smooth slide-in animation. |
| 12 | Verify the new employee appears in the employee list | "John Doe" is visible with correct department, job title, and status. |

## 6. Postconditions
- A new employee record exists in the database with all mandatory fields, auto-generated employee_no, status "active", correct tenant_id, and populated audit columns.
- The employee appears in the employee list for the "acme" tenant.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
