---
id: TC-LV-043
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-043: Responsive UI -- entitlement matrix collapses to card list on mobile

## 1. Test Objective
Verify that the entitlement rules matrix view collapses into a card-based list grouped by leave type when viewed on mobile viewports (360px), as specified in the UI/UX notes. The desktop matrix (rows = leave types, columns = departments/levels, cells = entitlement days) must remain usable and readable at all breakpoints.

## 2. Related Requirements
- User Story: US-LV-002
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with at least 3 leave types and 5 entitlement rules across departments/levels.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Viewport | Width | Expected Layout |
|----------|-------|-----------------|
| Mobile (small) | 360px | Card list grouped by leave type |
| Mobile (large) | 428px | Card list grouped by leave type |
| Tablet | 768px | Transitional (condensed matrix or card list) |
| Desktop | 1280px | Full matrix (rows x columns) |
| Large desktop | 1920px | Full matrix with comfortable spacing |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open entitlement configuration page at 1920px viewport | Full matrix displayed: leave types as rows, departments/levels as columns, entitlement days in cells. Inline editing available. |
| 2 | Resize to 1280px | Matrix still visible; columns may be slightly narrower. All data readable. |
| 3 | Resize to 768px (tablet) | Matrix transitions to condensed view or card list. All entitlement rules still visible and accessible. |
| 4 | Resize to 360px (mobile) | Matrix collapses to card-based list. Cards grouped by leave type. Each card shows: department, job level, entitlement days. |
| 5 | Verify card list at 360px shows all rules | All entitlement rules from the matrix are present in the card list. No data lost. |
| 6 | Verify inline editing works on mobile cards | Tapping a card allows editing the entitlement days (or navigates to an edit form). |
| 7 | Verify "Add Rule" action is accessible on mobile | "Add Rule" button or FAB is visible and tappable at 360px. |
| 8 | Verify no horizontal scrollbar at 360px | Content fits within the viewport width. |
| 9 | Verify filter/sort controls are accessible on mobile | Notion-like filter/sort/group controls are accessible via a collapsible panel or bottom sheet. |

## 6. Postconditions
- The entitlement UI is fully functional and readable across all viewport sizes.
- No data is hidden or inaccessible at any breakpoint.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
