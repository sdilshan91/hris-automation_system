---
id: TC-LV-020
user_story: US-LV-001
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-020: Cross-browser compatibility for leave types management page

## 1. Test Objective
Verify that the leave types management page (list, create, edit, reorder) renders correctly and functions properly across supported browsers: Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Leave Types configuration page is accessible.
- Latest stable versions of Chrome, Edge, Firefox, and Safari are available for testing.

## 4. Test Data
| Browser | Version | Platform |
|---------|---------|----------|
| Chrome | Latest stable | Windows/macOS |
| Edge | Latest stable | Windows |
| Firefox | Latest stable | Windows/macOS |
| Safari | Latest stable | macOS |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open Leave Types page in Chrome | Page renders correctly: table/card list, color tags, inline toggles, action buttons. No layout issues. |
| 2 | Create a new leave type in Chrome | Slide-over opens, form works, save succeeds. Animations smooth. |
| 3 | Repeat steps 1-2 in Edge | Same rendering and functionality as Chrome. |
| 4 | Repeat steps 1-2 in Firefox | Same rendering. Verify no Firefox-specific CSS issues (flexbox, grid). |
| 5 | Repeat steps 1-2 in Safari | Same rendering. Verify no Safari-specific issues (date inputs, animations, backdrop-filter). |
| 6 | Test drag-and-drop reorder in all browsers | Drag-and-drop works smoothly in all browsers (or arrow-button reorder as fallback). |
| 7 | Verify color picker in all browsers | Color picker renders and functions in all browsers. |

## 6. Postconditions
- Leave types management page works consistently across all supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
