---
id: TC-CHR-132
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-132: Pagination boundary -- 55 employees at page size 20 yields 3 pages with correct counts

## 1. Test Objective
Verify that with exactly 55 employees and a page size of 20, the directory correctly produces 3 pages: page 1 with 20 items, page 2 with 20 items, and page 3 with 15 items. Total count is displayed as 55 throughout. This validates AC-4, FR-5.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Exactly 55 active employee records exist in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total employees | 55 | Exactly 55 active |
| Page size | 20 | Default |
| Expected pages | 3 | ceil(55/20) = 3 |
| Page 1 count | 20 | |
| Page 2 count | 20 | |
| Page 3 count | 15 | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory with default settings | Page 1 loads. |
| 2 | Verify page 1 | 20 employee cards displayed. Pagination text: "Showing 1-20 of 55 employees". |
| 3 | Verify pagination controls | Page numbers 1, 2, 3 are visible. "Prev" is disabled. "Next" is enabled. |
| 4 | Click page 2 | Page 2 loads with 20 employee cards. "Showing 21-40 of 55 employees". Both "Prev" and "Next" are enabled. |
| 5 | Verify no duplicates between page 1 and page 2 | No employee_no from page 1 appears on page 2. |
| 6 | Click page 3 | Page 3 loads with 15 employee cards. "Showing 41-55 of 55 employees". "Prev" is enabled; "Next" is disabled. |
| 7 | Verify no duplicates between page 2 and page 3 | No employee_no from pages 1 or 2 appears on page 3. |
| 8 | Verify total count consistency | All three pages show "of 55 employees" in the pagination text. |
| 9 | Send `GET /api/v1/tenant/employees/directory?page=3&pageSize=20` | Response: `total: 55`, `page: 3`, `pageSize: 20`, `data` array with 15 items. |
| 10 | Change page size to 50 via page-size selector | Directory reloads; page 1 shows 50 cards, page 2 shows 5. Pagination updates to 2 pages. |
| 11 | Change page size to 10 | Directory shows 6 pages (10, 10, 10, 10, 10, 5). |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
