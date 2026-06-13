---
id: TC-LV-018
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-018: Responsive UI at 360px -- stacked form and accordion Advanced section

## 1. Test Objective
Verify that the leave type configuration UI is fully responsive at 360px viewport width: form fields stack vertically, the Advanced section collapses into an accordion, and all functionality remains accessible.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8

## 3. Preconditions
- Leave Types configuration page is accessible.
- Browser viewport set to 360px width (mobile simulation).
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Parameter | Value | Notes |
|-----------|-------|-------|
| Viewport Width | 360px | Mobile minimum |
| Viewport Height | 640px | Standard mobile height |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set browser viewport to 360px x 640px and navigate to Leave Types page | Leave types list renders in a card-based layout (not wide table). Color tags visible. Inline toggle for active/inactive accessible. |
| 2 | Click "Add Leave Type" button | Slide-over panel opens as a full-screen or near-full-screen overlay (not side panel) on mobile. |
| 3 | Verify form field layout | Fields stack vertically. Each group (Basic Info, Entitlement Rules, Carry-Forward, Document Rules, Advanced) is clearly separated. |
| 4 | Verify "Advanced" section is collapsed into an accordion | Advanced section (encashment, half-day, hourly, gender, max consecutive, negative balance) is collapsed by default. Tapping the header expands it. |
| 5 | Expand the Advanced accordion section | Fields within Advanced stack vertically. All toggle switches and dropdowns are touch-friendly (min 44px tap target). |
| 6 | Fill in all fields and submit the form | Form submits successfully. No horizontal scrolling required at any point. |
| 7 | Verify the save button is accessible without scrolling past content | Save button is sticky at the bottom or always visible. |
| 8 | Verify color picker is usable on mobile | Color picker opens and allows selection without overflow. |

## 6. Postconditions
- Leave type created successfully from mobile viewport.
- All UI elements are accessible at 360px width.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
