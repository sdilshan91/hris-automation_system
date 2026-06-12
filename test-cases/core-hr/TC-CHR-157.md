---
id: TC-CHR-157
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-157: Pan and zoom interactions on desktop and mobile

## 1. Test Objective
Verify that the org chart supports pan (mouse drag) and zoom (scroll wheel) on desktop, and pinch-zoom on mobile. Zoom controls (+, -, Fit to screen) in the floating toolbar work correctly. This validates FR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-2
- UI/UX Notes: Floating toolbar with zoom controls

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart with at least 10 nodes spanning multiple levels is rendered.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Node Count | 10+ | Enough to exceed viewport |
| Desktop | Chrome latest | Mouse interactions |
| Mobile | Chrome on Android | Touch interactions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page on desktop | Org chart renders within a zoomable canvas container. |
| 2 | Click and drag on an empty area of the canvas | The canvas pans in the drag direction; the cursor changes to a grab/grabbing icon. |
| 3 | Release mouse button | Panning stops; the canvas retains its new position. |
| 4 | Scroll mouse wheel up on the canvas | The chart zooms in; nodes appear larger. Zoom level indicator updates if present. |
| 5 | Scroll mouse wheel down on the canvas | The chart zooms out; nodes appear smaller. |
| 6 | Click the "+" button in the floating toolbar | The chart zooms in by one increment. |
| 7 | Click the "-" button in the floating toolbar | The chart zooms out by one increment. |
| 8 | Click the "Fit to screen" button | The entire visible tree is scaled and positioned to fit within the viewport. All visible nodes are within bounds. |
| 9 | On a touch device, perform a pinch-zoom gesture on the canvas | The chart zooms in/out corresponding to the pinch direction. |
| 10 | On a touch device, perform a single-finger drag on empty canvas area | The canvas pans in the drag direction. |

## 6. Postconditions
- No data was modified.
- Zoom and pan state are retained during the session.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
