---
id: TC-CHR-067
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-067: Duplicate email in same tenant rejected (AC-3, FR-3)

## 1. Test Objective
Verify that the system prevents creating a new employee with an email address that already exists for another employee in the same tenant, displaying the validation error "An employee with this email already exists."

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- An employee with email "john.doe@example.com" already exists in the "acme" tenant.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing email | john.doe@example.com | Already in use by another employee |
| first_name | Jane | New employee attempt |
| last_name | Smith | New employee attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module and click "Add Employee" | Multi-step wizard opens. |
| 2 | Fill in first_name = "Jane", last_name = "Smith", email = "john.doe@example.com" | Fields accept the typed values. |
| 3 | Fill in all other mandatory fields (date_of_joining, department, job_title, employment_type) | Fields populated. |
| 4 | Submit the form | Validation error is displayed. |
| 5 | Verify the error message reads: "An employee with this email already exists." | Exact message matches AC-3 specification. |
| 6 | Verify submission is prevented (no record created) | The form remains open; no new employee record exists in the database. |
| 7 | Verify the error appears inline below the email field with red accent | UI/UX note: validation errors appear inline with shake animation. |

## 6. Postconditions
- No new employee record is created.
- The existing employee with "john.doe@example.com" is unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
