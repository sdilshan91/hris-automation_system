---
id: TC-CHR-243
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-243: Duplicate email within the same import file -- second occurrence flagged as error

## 1. Test Objective
Verify that when an import file contains two rows with the same email address, the first row is imported successfully and the second row is flagged as a validation error with a clear "duplicate email" message referencing the row number. This validates BR-2 and FR-3 (email uniqueness within file).

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Required departments and job titles exist in tenant "acme".
- No employee with email "duplicate@acme.test" exists in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | duplicate_email_test.csv | 5 rows: rows 2 and 4 have same email |
| Row 2 | Jane,Doe,duplicate@acme.test,...,Engineering,Software Engineer,Full-Time | First occurrence -- should succeed |
| Row 4 | John,Smith,duplicate@acme.test,...,Marketing,Marketing Manager,Full-Time | Second occurrence -- should fail |
| Rows 1, 3, 5 | Unique emails | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page, select `duplicate_email_test.csv`, and click "Import". | System begins processing. |
| 2 | Wait for processing to complete. | Amber summary: "4 of 5 records imported successfully. 1 record failed." |
| 3 | Verify the error table. | One error entry: Row 4, field `email`, error "Duplicate email 'duplicate@acme.test' -- already appears in row 2 of this file." |
| 4 | Query the `employees` table. | 4 employees created. Row 2's Jane Doe exists with email "duplicate@acme.test". Row 4's John Smith was NOT created. |
| 5 | Verify no partially created record exists for row 4. | No orphan or incomplete record for the duplicate row. |

## 6. Postconditions
- 4 employees created. The duplicate email row was rejected.
- Error report correctly identifies the duplicate.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
