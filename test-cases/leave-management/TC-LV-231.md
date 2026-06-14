---
id: TC-LV-231
user_story: US-LV-011
module: Leave Management
priority: medium
type: e2e
status: draft
created: 2026-06-14
---

# TC-LV-231: LOP management screen renders and functions across browsers and is usable down to 360px (§8 — desktop-optimized, mobile-accessible)

## 1. Test Objective
Verify the LOP management page renders and operates consistently on Chrome, Edge, Firefox, and Safari, and remains usable (accessible, no clipping) from 360px up to 1920px, acknowledging §8's note that complex bulk actions are desktop-optimized but the screen must remain accessible on mobile.

## 2. Related Requirements
- User Story: US-LV-011
- UI/UX Notes §8 (LOP section; mobile accessible, desktop-optimized for complex actions)

## 3. Preconditions
- HR Officer "Asha" authenticated; LOP management screen populated with entries and filters.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | latest |
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the LOP management screen on each browser at 1280px | Layout, filters (auto/HR-assigned/employee-requested), the LOP list, and bulk-action entry points render identically; no console errors. |
| 2 | Resize to 360px | Content reflows to a usable mobile layout (list readable, primary actions reachable); no horizontal-scroll trap or clipped controls; complex bulk actions degrade gracefully (e.g. simplified flow) but remain accessible. |
| 3 | Exercise the LOP filters and open the assign-LOP / override action on each browser | Interactions behave consistently; the date pickers and multi-select work on all four browsers. |
| 4 | Verify the red/orange LOP highlight + non-color cue renders correctly on each browser | The highlight and its text/icon cue appear consistently across browsers. |

## 6. Postconditions
- The LOP management screen is cross-browser consistent and responsive/usable from 360px to 1920px.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
