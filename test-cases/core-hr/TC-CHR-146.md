---
id: TC-CHR-146
user_story: US-CHR-003
module: Core HR
priority: medium
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-146: WCAG 2.1 AA keyboard navigation for filters and pagination (NFR-6)

## 1. Test Objective
Verify that the Employee Directory meets WCAG 2.1 AA standards, specifically: keyboard navigation for search bar, filters, view toggle, sort controls, pagination, and employee cards/rows. Screen reader compatibility for all interactive elements. This validates NFR-6.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist.
- Screen reader (NVDA or VoiceOver) is enabled.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Testing tool | axe-core / Lighthouse | Automated accessibility scan |
| Screen reader | NVDA (Windows) / VoiceOver (macOS) | Manual testing |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory using keyboard only (Tab key) | Focus moves sequentially through: search bar, filter button, view toggle, sort selector, export button. |
| 2 | Press Tab to focus the search bar | Search bar has a visible focus ring (outline). |
| 3 | Type a search term using keyboard | Search works without mouse interaction. |
| 4 | Tab to the filter button and press Enter | Filter panel opens. Filter controls (dropdowns, checkboxes) are keyboard-navigable. |
| 5 | Select a filter option using keyboard (Arrow keys + Enter/Space) | Filter is applied; focus returns to the main content or filter chips. |
| 6 | Tab to a filter chip "x" button and press Enter | Filter is removed. |
| 7 | Tab to pagination controls | Focus moves to page number buttons and prev/next arrows. |
| 8 | Press Enter on "Next" button | Page 2 loads; focus moves to the first card/row of the new page. |
| 9 | Verify aria-label on pagination | "Previous page", "Next page", "Page 1", "Page 2" etc. are announced by screen reader. |
| 10 | Verify employee cards have accessible labels | Screen reader announces employee name, department, and status for each card. |
| 11 | Run axe-core automated scan | Zero critical or serious violations. |
| 12 | Verify color contrast | All text meets WCAG 2.1 AA contrast ratio (4.5:1 for normal text, 3:1 for large text). Status badges are distinguishable for color-blind users. |
| 13 | Verify the "Showing X-Y of Z employees" text is announced | The live region updates are announced by the screen reader when page changes. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
