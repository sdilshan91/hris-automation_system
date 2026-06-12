---
id: TC-CHR-241
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-241: Upload valid Excel (.xlsx) file with 10 rows -- all employees created successfully

## 1. Test Objective
Verify that the system accepts and correctly processes an Excel (.xlsx) file for bulk import, creating all valid employee records identically to CSV import. This confirms FR-1 support for both CSV and Excel formats.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-3, FR-5, FR-6
- Business Rules: BR-6 (ClosedXML for Excel parsing)

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Departments and job titles referenced in the Excel file exist in tenant "acme".
- No employees with the same emails as in the file already exist in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | valid_10_employees.xlsx | Excel format, 10 valid rows |
| File Size | ~15 KB | Well under 25 MB limit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page and advance to Step 2 (Upload File). | Upload zone is visible accepting CSV and Excel files. |
| 2 | Select `valid_10_employees.xlsx` via the file picker. | File name "valid_10_employees.xlsx" and size are displayed. The "Import" button becomes active. |
| 3 | Click "Import". | The system processes the Excel file synchronously (10 rows <= 500 threshold). |
| 4 | Wait for processing to complete. | Green summary banner: "10 of 10 records imported successfully." No error table. |
| 5 | Query the `employees` table for the 10 new records. | All 10 exist with correct `tenant_id`, auto-generated `employee_no`, resolved `department_id`, and resolved `job_title_id`. |
| 6 | Verify the Excel parser (ClosedXML) handled date columns correctly. | `date_of_joining` values are correctly parsed from Excel date format (not stored as serial numbers). |

## 6. Postconditions
- 10 new employee records created from Excel file.
- Audit log entry recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
