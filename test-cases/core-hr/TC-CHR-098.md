---
id: TC-CHR-098
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-098: Responsive design -- form at 360px, 768px, 1024px, 1920px (NFR-4)

## 1. Test Objective
Verify that the employee creation form is fully responsive from 360px to 1920px (and up to 4K), with appropriate layout changes at mobile, tablet, and desktop breakpoints as specified in UI/UX notes.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport 1 | 360px wide | Mobile (smallest supported) |
| Viewport 2 | 768px wide | Tablet breakpoint |
| Viewport 3 | 1024px wide | Desktop small |
| Viewport 4 | 1920px wide | Desktop large |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set browser viewport to 360px and open the Add Employee form | Full-width single-column layout. Steps become a vertical stepper (per UI/UX notes). Cards take full width. All fields are visible and usable. No horizontal scroll. |
| 2 | Tab through and submit the form at 360px | All interactions work correctly. Buttons are tap-friendly (min 44x44px touch target). |
| 3 | Set browser viewport to 768px and open the Add Employee form | Layout adapts to tablet size. May show 1-2 columns. Progress indicator remains visible. |
| 4 | Set browser viewport to 1024px | Desktop layout with possible multi-column form sections. Card-based wizard with horizontal stepper. |
| 5 | Set browser viewport to 1920px | Full desktop layout. Cards centered with appropriate max-width. No excessive whitespace or stretching. |
| 6 | Verify the profile photo upload zone adapts at each viewport | Circular crop preview scales appropriately. Drag-and-drop zone is usable at all sizes. |
| 7 | Verify validation error messages are visible at all viewport sizes | Inline errors are not clipped or hidden at any breakpoint. |

## 6. Postconditions
- The form is fully functional and visually correct at all tested viewport sizes.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
