---
id: TC-CHR-129
user_story: US-CHR-003
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-129: Filter by department and status with chips and URL params (happy path)

## 1. Test Objective
Verify that applying department and status filters correctly narrows the directory results, displays filter chips with removal capability, and persists filter state in URL query parameters for shareability. This validates AC-3, FR-2, FR-6.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2, FR-6
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist across departments: Engineering (15 active, 3 terminated), Marketing (10 active, 2 probation), HR (5 active), Finance (15 active).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter: Department | Engineering | 18 total, 15 active |
| Filter: Status | active | Across all departments |
| Combined result | 15 | Active employees in Engineering |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory page | Full directory loads. |
| 2 | Click the "Filters" button | Filter panel opens (slide-out or inline) showing: department (multi-select), status (multi-select), job title (multi-select), employment type (multi-select), location, date of joining range. |
| 3 | Select "Engineering" from the department filter | Department chip appears in the selection. |
| 4 | Select "active" from the status filter | Status chip appears in the selection. |
| 5 | Click "Apply Filters" | Directory reloads showing only active Engineering employees. |
| 6 | Verify result count | "Showing 1-15 of 15 employees" is displayed. |
| 7 | Verify filter chips below the search bar | Two chips are visible: "Department: Engineering" and "Status: active", each with an "x" button. |
| 8 | Verify URL query parameters | URL includes `?departments=Engineering&statuses=active`. |
| 9 | Copy the URL, open in a new tab (same authenticated session) | The directory loads with the same filters pre-applied: Engineering + active, 15 results. Filter chips are pre-populated. |
| 10 | Click the "x" on the "Department: Engineering" chip | The department filter is removed; directory shows all active employees across all departments. |
| 11 | Verify URL updates | `departments` parameter is removed from URL; `statuses=active` remains. |
| 12 | Use browser back button | Previous filter state (Engineering + active) is restored. |

## 6. Postconditions
- No data was modified.
- Filter state is preserved in URL query parameters.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
