---
id: TC-CHR-136
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-136: Sort by date of joining descending and other sort options

## 1. Test Objective
Verify that the directory supports sorting by name (A-Z, Z-A), employee_no, date of joining, and department, and that the sort order is correctly reflected in the results and URL. This validates FR-4.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-4, FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist with varied dates of joining (ranging from 2020-01-15 to 2026-06-01) and different departments.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sort: default | name ascending | AC-1 |
| Sort: date_of_joining desc | Most recent first | |
| Sort: employee_no asc | EMP-0001 first | |
| Sort: department asc | Alphabetical by department | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory | Default sort is name ascending. |
| 2 | Change sort to "Date of Joining (Newest First)" | Directory reloads; the first employee has the most recent date_of_joining. |
| 3 | Verify order | Each subsequent employee's date_of_joining is the same as or earlier than the previous. |
| 4 | Verify URL | URL includes `?sort=dateOfJoining&sortDirection=desc`. |
| 5 | Change sort to "Employee No (Ascending)" | First employee has the lowest employee_no (e.g., EMP-0001). |
| 6 | Change sort to "Department (A-Z)" | Employees are grouped by department name alphabetically. |
| 7 | Change sort to "Name (Z-A)" | First employee has the last name closest to "Z". |
| 8 | Verify sort persists across pagination | On page 2, the sort order continues correctly from page 1. |

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
