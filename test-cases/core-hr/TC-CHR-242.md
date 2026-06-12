---
id: TC-CHR-242
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-242: Partial failure -- 8 valid + 2 invalid rows; 8 created, 2 reported with row number, field, and error

## 1. Test Objective
Verify that when an import file contains a mix of valid and invalid rows, the system imports all valid rows and returns a detailed error report for invalid rows. The error report must list row number, field name, and error description for each failed row. The user can download the error report as a CSV. This validates AC-3, FR-4, and FR-8.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4, FR-8
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Department "Engineering" exists in tenant "acme". Department "Nonexistent Dept" does NOT exist.
- Job title "Software Engineer" exists in tenant "acme".
- No employees with the emails in the file already exist in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | mixed_10_employees.csv | 10 rows: 8 valid, 2 invalid |
| Row 3 (invalid) | Missing `last_name` (required field) | Expected error: required field missing |
| Row 7 (invalid) | department_name = "Nonexistent Dept" | Expected error: department not found (BR-3) |
| Rows 1-2, 4-6, 8-10 | All valid with existing departments and job titles | Should be imported successfully |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page, select `mixed_10_employees.csv`, and click "Import". | System begins processing the 10-row file synchronously. |
| 2 | Wait for processing to complete. | Step 3 (Review Results) displays an amber summary banner: "8 of 10 records imported successfully. 2 records failed." |
| 3 | Verify the error table below the summary. | An error table is displayed with 2 rows: (1) Row 3, field `last_name`, error "last_name is required"; (2) Row 7, field `department_name`, error "Department 'Nonexistent Dept' not found in tenant." |
| 4 | Query the `employees` table for new records. | Exactly 8 new employees exist. Rows 1-2, 4-6, 8-10 were imported. Rows 3 and 7 were skipped. |
| 5 | Verify the "Download Error Report" button is visible. | A button/link to download the error report as CSV is present below the error table. |
| 6 | Click "Download Error Report". | A CSV file is downloaded containing columns: `row_number`, `field`, `error`. It has 2 data rows matching the error table content. |
| 7 | Verify audit log for the import. | Audit log entry: file name = "mixed_10_employees.csv", total rows = 10, success count = 8, failure count = 2 (FR-10). |

## 6. Postconditions
- 8 new employees created in "acme" tenant.
- 2 rows were skipped and reported with specific error details.
- Error report downloadable as CSV.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
