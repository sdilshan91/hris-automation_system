---
id: TC-CHR-314
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-314: Responsive 360px management page with arrow-button reorder

## 1. Test Objective
Verify that the Custom Fields management page is fully responsive at 360px viewport width. On mobile, drag-and-drop reordering is replaced by up/down arrow buttons. Form fields stack vertically. The plan limit indicator and all field management actions remain accessible. This validates NFR-4 and the UI/UX notes for mobile behavior.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with 3 custom fields defined.
- Tenant Admin is authenticated.
- Browser viewport set to 360px width (mobile emulation).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport | 360px x 640px | Mobile breakpoint |
| Custom Fields | 3 fields | T-Shirt Size (1), Project Code (2), Union ID (3) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields at 360px viewport. | The page renders without horizontal scrolling. Fields are listed vertically as cards. |
| 2 | Verify drag handle is not visible. | Drag-and-drop handles are hidden at mobile width. |
| 3 | Verify up/down arrow buttons are visible on each field card. | Each field card has up-arrow and down-arrow buttons for reordering. |
| 4 | Click the up-arrow on "Project Code" (order 2). | "Project Code" moves to order 1. "T-Shirt Size" moves to order 2. The list updates smoothly. |
| 5 | Click "Add Custom Field" button. | The creation form opens (modal or slide-over) with fields stacked vertically at full width. |
| 6 | Verify the field type selector is usable at 360px. | Type selector cards/icons are arranged in a grid that fits within 360px (e.g., 2-3 columns or a scrollable list). |
| 7 | Verify the plan limit indicator is visible. | "X of Y custom fields used" indicator is displayed and readable. |

## 6. Postconditions
- The management page is fully functional at 360px viewport.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
