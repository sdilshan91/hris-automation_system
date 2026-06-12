---
id: TC-CHR-133
user_story: US-CHR-003
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-133: Export filtered employee list as CSV with correct columns and row count

## 1. Test Objective
Verify that an HR Officer can filter the directory to a subset of employees, export as CSV, and the resulting file contains exactly the filtered number of rows with columns matching the displayed fields and scoped to the current tenant only. This validates AC-5, FR-8, BR-4.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist; 10 are in the "Marketing" department and active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter: Department | Marketing | 10 active employees |
| Export format | CSV | |
| Expected rows | 10 | Plus 1 header row |
| Expected columns | employee_no, first_name, last_name, email, phone, department_name, job_title_name, status, date_of_joining, location | HR Officer full visibility |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee Directory and filter by Department = "Marketing" | Directory shows 10 employees. |
| 2 | Click the "Export" button | A dropdown/dialog appears with format options: CSV, Excel. |
| 3 | Select "CSV" | A file download begins. |
| 4 | Verify the downloaded file | File name contains "employee-directory" and ".csv" extension. |
| 5 | Open the CSV file | File has UTF-8 BOM encoding. |
| 6 | Verify header row | Columns include: employee_no, first_name, last_name, email, phone, department_name, job_title_name, status, date_of_joining, location. |
| 7 | Verify data row count | Exactly 10 data rows (excluding header). |
| 8 | Verify all 10 employees are from "Marketing" department | Every row has "Marketing" in the department column. |
| 9 | Verify no employee from another department appears | No rows with department other than "Marketing". |
| 10 | Verify CSV escaping | Fields with commas or quotes are properly RFC 4180 escaped. |
| 11 | Verify the API call `GET /api/v1/tenant/employees/directory/export?format=Csv&departments=Marketing` was made | Response content-type is `text/csv`. |

## 6. Postconditions
- No data was modified.
- Exported file reflects the filtered, tenant-scoped dataset.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
