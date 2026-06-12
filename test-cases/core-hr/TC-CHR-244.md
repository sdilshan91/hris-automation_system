---
id: TC-CHR-244
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-244: Non-existent department_name in import row -- row rejected with descriptive error

## 1. Test Objective
Verify that when a row in the import file references a `department_name` that does not exist in the tenant's department list, that row is rejected with a clear error message naming the unrecognized department. This validates BR-3 and FR-3.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Department "Engineering" exists. Department "Phantom Department" does NOT exist in tenant "acme".
- Job title "Software Engineer" exists in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | bad_department.csv | 3 rows: row 2 has non-existent department |
| Row 2 | John,Smith,john@acme.test,...,Phantom Department,Software Engineer,Full-Time | Invalid department |
| Rows 1, 3 | Valid departments | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `bad_department.csv` and click "Import". | System processes the file. |
| 2 | Wait for processing to complete. | Amber summary: "2 of 3 records imported successfully. 1 record failed." |
| 3 | Verify the error table. | Error entry: Row 2, field `department_name`, error "Department 'Phantom Department' not found in tenant." |
| 4 | Query the `employees` table. | 2 employees created (rows 1, 3). Row 2 not imported. |

## 6. Postconditions
- 2 employees created. Row with invalid department was skipped and reported.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
