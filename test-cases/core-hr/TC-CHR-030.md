---
id: TC-CHR-030
user_story: US-CHR-004
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-030: Department management UI responsive design (360px to 1920px)

## 1. Test Objective
Verify that the Department management page is fully responsive across screen widths from 360px (mobile) to 1920px (desktop), with appropriate layout adaptations as described in the UI/UX notes: table collapses to card list on mobile, tree view uses collapsible accordions.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-3
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with departments forming a hierarchy.
- A user with Tenant Admin role is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport 360px | Mobile portrait | Min supported |
| Viewport 768px | Tablet portrait | Mid breakpoint |
| Viewport 1024px | Tablet landscape / small desktop | |
| Viewport 1920px | Full desktop | Max common |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set viewport to 360px width and navigate to Departments page | Table collapses to a card list layout per UI/UX notes. Each department is shown as a card with Name, Status, and actions. |
| 2 | Verify "Add Department" button is visible and tappable on mobile | Button is accessible; may be a floating action button (FAB) or in a toolbar. |
| 3 | Open the create form on 360px viewport | Slide-over panel fills the entire screen (full-width modal on mobile). All fields are usable. |
| 4 | Toggle to tree view on 360px viewport | Tree uses collapsible accordions per UI/UX notes. Tap to expand/collapse. |
| 5 | Set viewport to 768px (tablet) | Layout adapts: table may show fewer columns; tree nodes have adequate tap targets. |
| 6 | Set viewport to 1024px | Slide-over panel appears as a 400px side panel. Table shows full columns. |
| 7 | Set viewport to 1920px | Full desktop layout: card-based table with all columns (Name, Parent, Manager, Employee Count, Status). Tree view with full indentation. |
| 8 | Verify no horizontal scrollbar at any viewport width | Content fits within the viewport at all tested widths. |
| 9 | Verify text does not overflow or get clipped | All department names and values are readable (truncated with ellipsis if necessary). |

## 6. Postconditions
- UI is usable at all tested viewport widths.
- No layout breaks or unusable elements.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
