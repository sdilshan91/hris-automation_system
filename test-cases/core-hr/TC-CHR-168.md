---
id: TC-CHR-168
user_story: US-CHR-006
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-168: Cross-browser compatibility for org tree (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the Organization Tree page renders correctly and all interactions (expand/collapse, pan/zoom, search, view toggle, export) work across major browsers: Chrome, Edge, Firefox, and Safari. This validates NFR-2 and NFR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-2, NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart with a 3-level hierarchy is available.
- Latest stable versions of Chrome, Edge, Firefox, and Safari are installed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chrome | Latest stable | Primary browser |
| Edge | Latest stable | Chromium-based |
| Firefox | Latest stable | Gecko engine |
| Safari | Latest stable | WebKit engine (macOS only) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Organization Tree page in Chrome | Tree renders correctly; SVG connector lines display; node cards are styled properly. |
| 2 | Perform expand/collapse, pan/zoom, search in Chrome | All interactions work without errors; animations are smooth. |
| 3 | Open the Organization Tree page in Edge | Tree renders identically to Chrome; all elements are properly positioned. |
| 4 | Perform expand/collapse, pan/zoom, search in Edge | All interactions work without errors. |
| 5 | Open the Organization Tree page in Firefox | Tree renders correctly; SVG paths are drawn; layout is correct. |
| 6 | Perform expand/collapse, pan/zoom, search in Firefox | All interactions work. Verify scroll-wheel zoom works (Firefox scroll behavior may differ). |
| 7 | Open the Organization Tree page in Safari | Tree renders correctly; WebKit-specific CSS is handled. |
| 8 | Perform expand/collapse, pan/zoom, search in Safari | All interactions work. Verify pinch-zoom works on trackpad. |
| 9 | Export PNG in each browser | Downloaded PNG is valid and contains the tree in all four browsers. |
| 10 | Verify no browser-specific console errors | No unhandled exceptions in any browser's developer console. |

## 6. Postconditions
- No data was modified.
- All browsers render the org tree consistently.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
