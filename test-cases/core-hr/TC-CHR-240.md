---
id: TC-CHR-240
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-240: Upload valid CSV with 10 rows -- all 10 employees created with correct tenant_id and auto-generated employee_no (happy path)

## 1. Test Objective
Verify that uploading a valid CSV file with 10 employee rows results in all 10 records being created in the database with `tenant_id` set from the authenticated session context (not the file), auto-generated `employee_no` values per the tenant's numbering pattern, and a success summary displayed: "10 of 10 records imported successfully." This is the primary happy path for AC-2.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-3, FR-4, FR-5, FR-6, FR-10
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`, subdomain `acme.yourhrm.com`, and sufficient employee capacity (plan limit > current count + 10).
- An HR Officer user (`hr-officer-uuid`) is authenticated in the "acme" tenant context.
- Departments referenced in the CSV ("Engineering", "Marketing") exist in tenant "acme".
- Job titles referenced in the CSV ("Software Engineer", "Marketing Manager") exist in tenant "acme".
- No employees with the same emails as in the CSV already exist in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | valid_10_employees.csv | 10 valid rows + header |
| File Size | ~2 KB | Well under 25 MB limit |
| Row 1 | Jane,Doe,jane.doe@acme.test,+12025551001,,Female,2026-01-10,Engineering,Software Engineer,Full-Time,, | All required fields present |
| Row 10 | Bob,Williams,bob.w@acme.test,,,Male,2026-03-15,Marketing,Marketing Manager,Part-Time,, | All required fields present |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page. | Page loads showing Step 1 (Download Template). |
| 2 | Advance to Step 2 (Upload File). Click the upload zone or use the file picker to select `valid_10_employees.csv`. | File name and size are displayed in the upload zone. The "Import" button becomes active. |
| 3 | Click "Import". | The system begins processing. For <= 500 rows, processing is synchronous. A spinner or progress indicator is shown briefly. |
| 4 | Wait for processing to complete. | Step 3 (Review Results) is displayed. A green summary banner shows: "10 of 10 records imported successfully." No error table is displayed. |
| 5 | Query the `employees` table for the 10 new records by email. | All 10 rows exist with `tenant_id` matching the acme tenant UUID. |
| 6 | Verify `employee_no` values on the 10 new records. | Each employee has a unique, auto-generated `employee_no` following the tenant's pattern (e.g., "EMP-0001" through "EMP-0010" or continuing from the last assigned number). No two share the same `employee_no`. |
| 7 | Verify `tenant_id` is the acme tenant UUID on all 10 records. | All records have the correct `tenant_id`. None have a `tenant_id` from the file content (it is ignored even if present). |
| 8 | Verify each employee's `department_id` resolves correctly. | Employees referencing "Engineering" have the correct Engineering department UUID; similarly for "Marketing". |
| 9 | Verify each employee's `job_title_id` resolves correctly. | Employees referencing "Software Engineer" have the correct job title UUID. |
| 10 | Verify default status is `active` for all 10 employees (no status column was provided). | All 10 have `status = 'active'` (BR-4). |
| 11 | Verify audit log entry for the import operation. | An audit log entry exists with: file name = "valid_10_employees.csv", total rows = 10, success count = 10, failure count = 0, actor = hr-officer-uuid (FR-10). |

## 6. Postconditions
- 10 new employee records exist in the "acme" tenant.
- Each has a unique `employee_no` and correct `tenant_id`.
- Audit log contains an entry for this import operation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
