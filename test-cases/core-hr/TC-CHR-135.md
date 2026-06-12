---
id: TC-CHR-135
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-135: View mode toggle between card/grid and table/list view

## 1. Test Objective
Verify that the user can switch between card/grid view and table/list view using the toggle buttons, and that both views display the same data with appropriate layout. This validates FR-3.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Default view | Card/Grid | Desktop default |
| Alternate view | Table/List | Toggle |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory | Directory loads in card/grid view (default on desktop). |
| 2 | Verify card view layout | Employee cards displayed in responsive grid (4 columns on desktop); each card has `rounded-xl shadow-sm`, circular avatar, name, title, department tag, status badge. |
| 3 | Verify hover effect on a card | Card lifts with `translateY(-2px)` and shadow increases to `shadow-md` over 150ms transition. |
| 4 | Click the "List/Table" view toggle button | Directory switches to table/list view. |
| 5 | Verify table view layout | Clean table with sticky header, alternating row shading, columns for avatar, name, employee_no, department, job title, status, location. |
| 6 | Verify row hover effect | Row background highlights on hover. |
| 7 | Verify same data in both views | Same 20 employees displayed on page 1, same sort order. |
| 8 | Click the "Card/Grid" view toggle button | Directory switches back to card view. |
| 9 | Verify URL state | URL includes `?view=card` or `?view=table` reflecting the current view mode. |

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
