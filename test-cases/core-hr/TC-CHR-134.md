---
id: TC-CHR-134
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-134: Export employee directory as Excel (.xlsx) with correct formatting

## 1. Test Objective
Verify that the Excel export produces a valid .xlsx file (via ClosedXML) with a single worksheet "Employee Directory", auto-fit columns, bold headers, and data matching the current filter and role-based visibility. This validates AC-5, FR-8.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 30 employees exist; filter set to status = "active" yields 25.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter: Status | active | 25 active employees |
| Export format | Excel | .xlsx via ClosedXML |
| Expected rows | 25 | Plus 1 header row |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee Directory and filter by Status = "active" | Directory shows 25 employees. |
| 2 | Click the "Export" button and select "Excel" | A file download begins. |
| 3 | Verify the downloaded file | File name contains "employee-directory" and ".xlsx" extension. |
| 4 | Open the .xlsx file | File is a valid Excel workbook (not corrupted). |
| 5 | Verify worksheet name | Single worksheet named "Employee Directory". |
| 6 | Verify header row | Row 1 contains bold column headers matching CSV columns: employee_no, first_name, last_name, email, phone, department_name, job_title_name, status, date_of_joining, location. |
| 7 | Verify data row count | Exactly 25 data rows (rows 2-26). |
| 8 | Verify auto-fit columns | Columns are auto-fitted to content width; no truncated or overlapping text. |
| 9 | Verify the API call `GET /api/v1/tenant/employees/directory/export?format=Excel&statuses=active` was made | Response content-type is `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
