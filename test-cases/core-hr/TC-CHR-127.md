---
id: TC-CHR-127
user_story: US-CHR-003
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-127: Employee directory loads paginated, sorted by name ascending (happy path)

## 1. Test Objective
Verify that when an HR Officer navigates to the Employee Directory page, a paginated card/grid of employees is displayed showing avatar, name, employee_no, department, job title, and status badge, sorted by name (last_name, first_name) ascending by default. This validates AC-1, FR-5, and FR-4.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-1
- Functional Requirements: FR-4, FR-5, FR-7
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- 25 active employee records exist in the "acme" tenant with names spanning "Adams" to "Zimmerman" to verify sort order.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full directory access |
| Employee Count | 25 | More than one page at default page size |
| Default Page Size | 20 | AC-4 |
| Default Sort | name ascending | AC-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/employees` | Directory page begins loading; skeleton shimmer cards are displayed. |
| 2 | Wait for page load to complete | Skeleton placeholders are replaced with employee cards in a responsive grid. |
| 3 | Verify card contents for the first employee | Card shows: circular avatar, full name, employee_no badge, department tag, job title, and status badge (color-coded). |
| 4 | Verify default sort order | Employees are listed alphabetically by name ascending; the first card starts with "A" names and the last card on page 1 is before any "Z" names. |
| 5 | Verify pagination controls at the bottom | Pagination bar shows: page numbers, prev/next arrows, "Showing 1-20 of 25 employees" text. |
| 6 | Verify page 1 shows exactly 20 employee cards | Count of visible cards equals 20. |
| 7 | Verify the API call `GET /api/v1/tenant/employees/directory?page=1&pageSize=20&sort=name&sortDirection=asc` was made | Response status is 200 OK; response body contains `data` (array of 20), `total: 25`, `page: 1`, `pageSize: 20`. |
| 8 | Click "Next" or page 2 | Page 2 loads with 5 remaining employee cards; "Showing 21-25 of 25 employees". |
| 9 | Verify sort continuity across pages | First employee on page 2 is alphabetically after the last employee on page 1. |
| 10 | Verify search bar is visible | Full-width search bar with search icon is present at the top. |
| 11 | Verify filter button is visible | "Filters" button exists next to the search bar. |
| 12 | Verify view toggle buttons | Grid/list toggle buttons are visible in the top-right corner, next to the Export button. |

## 6. Postconditions
- No data was modified.
- URL reflects the current page state: `?page=2&pageSize=20`.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
