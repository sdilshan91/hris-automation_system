---
id: TC-LV-148
user_story: US-LV-007
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-LV-148: Calendar view responsive (360px), keyboard/screen-reader navigable, color not the sole type indicator (NFR-4, §8)

## 1. Test Objective
Verify the holiday calendar/list UI meets WCAG 2.1 AA and NFR-4: it is functional and readable on mobile (360px, collapsing to a compact list-by-month view), fully keyboard- and screen-reader-operable, and holiday type is conveyed by more than color alone (label/icon in addition to the blue/orange/green coding) -- across target browsers.

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-4
- UI/UX Notes (Section 8)

## 3. Preconditions
- Tenant "acme" with 2026 holidays of all three types; user authenticated with `Holiday.View`.
- Standard: WCAG 2.1 AA. Browsers: Chrome, Edge, Firefox, Safari.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive |
| Type encoding | color + text/icon | not color-only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the calendar at 360px | Calendar collapses to a compact list-by-month view; all holidays readable, no horizontal scroll/overlap; the view/list toggle and year nav remain usable. |
| 2 | Navigate by keyboard only (Tab/Enter/Space/arrows) | Year nav, view toggle, calendar cells, and list rows are reachable and operable with a visible focus ring; add/edit affordances reachable for permitted users. |
| 3 | Verify type is not color-only | Each holiday marker/row carries a text label or icon for its type (Public/Restricted/Optional) in addition to the color; contrast >= 4.5:1 for text. |
| 4 | Run an axe audit + screen-reader pass across Chrome, Edge, Firefox, Safari | No critical violations; markers, type, date, and name are announced meaningfully across all four browsers. |

## 6. Postconditions
- The holiday calendar is responsive, keyboard/SR-accessible, and conveys type beyond color across browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
