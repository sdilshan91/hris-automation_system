---
id: TC-CHR-169
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-169: Tree is read-only -- no drag-and-drop; links redirect to management pages

## 1. Test Objective
Verify that the org tree is read-only: users cannot reorganize the hierarchy via drag-and-drop. Modifications must be made through the department and employee management pages, which are linked from the detail panel. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-006
- Business Rules: BR-5
- Acceptance Criteria: AC-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart rendered with "Corp" (root) -> "Engineering", "Sales".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Source Node | Engineering | Attempt to drag |
| Target Node | Sales | Attempt to drop under |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders with "Corp" -> "Engineering", "Sales". |
| 2 | Attempt to click-and-drag "Engineering" node toward "Sales" | No drag operation initiates. The node does not move. The cursor does not change to a drag cursor. No drop target indicators appear. |
| 3 | Verify no "drop zone" visual indicators appear on any node | No highlighted borders, overlays, or insertion markers appear during the drag attempt. |
| 4 | Click on "Engineering" node to open detail panel | Detail panel opens showing department info. |
| 5 | Verify the detail panel contains a link to the department management page | A link/button such as "Manage Department" or "Edit in Department Settings" is present. |
| 6 | Click the management link | Browser navigates to the department management page (e.g., `/departments/{engineering-id}`). |
| 7 | Verify no "edit" or "move" controls exist directly on the tree nodes | No inline edit icons, drag handles, or context menus with "Move" options are present on tree nodes. |

## 6. Postconditions
- No data was modified.
- The org tree hierarchy remains unchanged.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
