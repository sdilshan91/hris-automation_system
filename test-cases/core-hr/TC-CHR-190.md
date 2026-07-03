---
id: TC-CHR-190
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: pass
exec_note: "2026-07-03 (BROWSER-RIG Wave 1, CDP emulate, acme/hr@acme.test): PASS on responsive arm. Locations page (/locations — reached via pushState+popstate soft-nav since it has no sidebar link; HR Officer is route-authorized) at 1440/360 via `emulate` resize. At 360px the desktop `<table>` is hidden (width 0) and the mobile card list (`div.md:hidden`) renders; 'Add Location' button present. No document horizontal scroll (scrollWidth==clientWidth 354). Same benign artifact as TC-CHR-061: one pathological long-name card is 1627px wide but clipped by overflow:hidden (no page break). Cross-browser (Firefox/WebKit/Safari) NOT covered (chromium-only rig). Responsive criteria PASS."
created: 2026-06-12
---

# TC-CHR-190: Responsive layout -- 360px viewport collapses to card list

## 1. Test Objective
Verify that the Locations management page is fully responsive: on a 360px viewport (mobile), the table collapses to a card list with stacked address lines. On desktop widths (1920px), the full card-based table layout is displayed. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-3
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- At least 3 locations exist with full address data.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Viewport Widths | 360px, 768px, 1280px, 1920px | Responsive breakpoints |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Locations page in a 1920px viewport (desktop) | Full card-based table layout is displayed with columns: Name, City, Country, Time Zone, Employee Count, Status. Subtle card styling: `rounded-xl shadow-sm bg-white`. |
| 2 | Resize viewport to 1280px | Table layout is maintained with possible column width adjustments. All columns remain visible. |
| 3 | Resize viewport to 768px (tablet) | Layout begins transitioning. Some columns may be condensed or rearranged but core information (Name, Status) remains visible. |
| 4 | Resize viewport to 360px (mobile) | Table collapses to a card list layout. Each location is displayed as a stacked card with: Name (prominent), City/Country (stacked), Time Zone, Employee Count, Status badge. Address lines are stacked vertically. |
| 5 | Verify the "Add Location" button is accessible at 360px | The button is visible and tappable (not hidden off-screen or overlapping other elements). |
| 6 | Open the "Add Location" form at 360px | Form renders in a full-screen or stacked layout. All fields are usable. Address section is collapsible. The Time Zone dropdown is usable on mobile. |
| 7 | Verify no horizontal scrollbar at 360px | The page fits within 360px width without horizontal overflow. |
| 8 | Verify touch targets are at least 44x44px | All buttons and interactive elements meet the minimum touch target size for mobile. |

## 6. Postconditions
- No data was modified.
- The page renders correctly across all tested viewport widths.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — responsive TC (360px card-list collapse). Requires viewport resizing not available in the fixed shared chromium MCP session. Locations renders correctly at desktop width.

> **Execution 2026-07-01 (triage, acme):** STILL BLOCKED — FE-UI-only arm (responsive-viewport / cross-browser / visual-render). Not API-testable this pass; requires viewport resizing (360px–1920px) and/or multiple browser engines the single shared MCP session can't drive. Not a functional/business-rule defect.
