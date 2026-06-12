---
id: TC-CHR-259
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-259: Download error report CSV after partial import -- file contains correct row numbers, fields, and errors

## 1. Test Objective
Verify that the downloadable error report CSV (FR-8) is correctly formatted with columns `row_number`, `field`, `error`, and that the content accurately reflects all validation failures from the import. Each failed row produces one or more entries in the report.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Import file `multi_error.csv` has 10 rows: 7 valid, 3 with different types of errors.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | multi_error.csv | 10 rows total |
| Row 3 | Missing `email` (required) | Validation error |
| Row 5 | department_name = "Ghost Dept" (non-existent) | Lookup error |
| Row 9 | Duplicate email (same as row 1) | Uniqueness error |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `multi_error.csv` and click "Import". | 7 imported, 3 failed. |
| 2 | Click "Download Error Report". | A CSV file is downloaded. |
| 3 | Open the CSV and verify headers. | First row: `row_number,field,error`. |
| 4 | Verify error entries. | Row 3: field = `email`, error = "email is required." Row 5: field = `department_name`, error = "Department 'Ghost Dept' not found in tenant." Row 9: field = `email`, error = "Email already exists" (or "Duplicate email"). |
| 5 | Verify the CSV is UTF-8 encoded. | File opens correctly with non-ASCII characters (if any) in error messages. |
| 6 | Verify row numbers in the error report correspond to the original file row numbers (header = row 1, data starts at row 2). | Row numbers match the source file positions, not a re-indexed count. |

## 6. Postconditions
- Error report accurately represents all failed rows with actionable information.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
