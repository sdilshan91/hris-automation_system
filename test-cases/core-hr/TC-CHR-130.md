---
id: TC-CHR-130
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-130: Search with no matches displays empty state

## 1. Test Objective
Verify that when a search query or filter combination returns zero results, the directory displays a user-friendly empty state illustration with a helpful message, rather than a blank page or error. This is a negative/edge-case test for AC-2 and FR-1.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1
- UI/UX Notes: Section 8 (empty state)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist in the "acme" tenant; none have "xyznonexistent" in their name, email, employee_no, or phone.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Search term | xyznonexistent123 | Guaranteed no match |
| Filter: Department | Engineering | Valid department |
| Filter: Status | terminated | No terminated employees in Engineering |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory page | Full directory loads with employees. |
| 2 | Type "xyznonexistent123" in the search bar | After 300ms debounce, API returns empty results. |
| 3 | Verify empty state UI | An illustration is displayed with the message "No employees found. Try adjusting your search or filters." |
| 4 | Verify pagination is hidden | No pagination bar is shown when there are zero results. |
| 5 | Verify total count | "Showing 0 of 0 employees" or equivalent zero-state text. |
| 6 | Clear search and apply filter combination: Department = "Engineering", Status = "terminated" | After applying filters, if no terminated employees exist in Engineering, the empty state is shown. |
| 7 | Verify empty state persists with filters | Same illustration and message displayed; filter chips still visible so the user can remove filters. |
| 8 | Click "x" on all filter chips | Directory returns to unfiltered state with all employees visible. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
