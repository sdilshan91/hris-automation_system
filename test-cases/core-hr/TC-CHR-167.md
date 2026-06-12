---
id: TC-CHR-167
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-167: Responsive layout at 360px falls back to accordion/vertical list

## 1. Test Objective
Verify that on mobile viewports (< 768px, specifically at 360px width), the org chart renders as a collapsible vertical list (accordion style) rather than a horizontal zoomable tree. Each level is indented with a left border line. This validates NFR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Mobile accordion layout

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart with hierarchy: "Corp" (root) -> "Engineering", "Sales" -> "Backend" (child of Engineering).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport Width | 360px | Mobile width |
| Viewport Height | 640px | Standard mobile height |
| Layout Expected | Accordion/vertical list | Not horizontal tree |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set the browser viewport to 360px x 640px (or use Chrome DevTools device emulation) | Mobile viewport is active. |
| 2 | Navigate to the Organization Tree page | The org chart renders as a vertical list, NOT a horizontal zoomable canvas. |
| 3 | Verify the layout is accordion-style | Each department is shown as a vertically stacked card/row. Child items are indented with a left border line. |
| 4 | Verify "Corp" is visible as the top-level item | "Corp" is displayed as an expandable accordion header. |
| 5 | Tap on "Corp" to expand | "Engineering" and "Sales" appear below "Corp" with indentation (left border line visible). |
| 6 | Tap on "Engineering" to expand | "Backend" appears below "Engineering" with further indentation. |
| 7 | Verify the zoom/pan controls are NOT displayed | The floating toolbar with +/-/Fit buttons is hidden on mobile. |
| 8 | Verify the search bar is still accessible | Search bar is present and functional at the top. |
| 9 | Verify the view toggle (Department/Reporting) is accessible | Toggle buttons are present, possibly reformatted as a dropdown or stacked buttons for mobile. |
| 10 | Resize the viewport to 768px | The layout transitions from accordion to the zoomable tree canvas. |
| 11 | Resize back to 360px | The layout reverts to the accordion/vertical list. |

## 6. Postconditions
- No data was modified.
- Layout adapts correctly when viewport changes.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
