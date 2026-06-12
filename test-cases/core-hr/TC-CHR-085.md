---
id: TC-CHR-085
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-085: Create employee with missing mandatory fields fails validation

## 1. Test Objective
Verify that submitting the employee creation form with one or more mandatory fields missing results in validation errors displayed inline below the respective fields. The form should not submit, and no employee record should be created.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-2 (negative path)
- Data Requirements: first_name, last_name, email, date_of_joining, department_id, job_title_id, employment_type (all required)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| first_name | (empty) | Missing required field |
| last_name | Doe | Provided |
| email | (empty) | Missing required field |
| date_of_joining | (empty) | Missing required field |
| department_id | (empty) | Missing required field |
| job_title_id | (not selected) | Missing required field |
| employment_type | (not selected) | Missing required field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Add Employee wizard | Wizard opens. |
| 2 | Leave first_name empty, fill last_name = "Doe", leave email empty | Fields are in the respective states. |
| 3 | Navigate to Employment Details and leave date_of_joining, department, job_title, and employment_type empty | Fields are empty. |
| 4 | Attempt to submit the form | Submission is prevented. |
| 5 | Verify validation errors appear inline below each missing mandatory field | Errors like "First name is required", "Email is required", "Date of joining is required", "Department is required", "Job title is required", "Employment type is required". |
| 6 | Verify errors appear with red accent and shake animation | Per UI/UX notes. |
| 7 | Verify no API call is made (client-side validation) or API returns 400/422 | No employee record is created in the database. |
| 8 | Fill in one field (first_name = "John") and re-submit | The error for first_name disappears; other errors remain. |

## 6. Postconditions
- No employee record is created.
- The form remains open with inline validation errors.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
