---
id: TC-CHR-190
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
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
