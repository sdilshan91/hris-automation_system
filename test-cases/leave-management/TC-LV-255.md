---
id: TC-LV-255
user_story: US-LV-012
module: Leave Management
priority: medium
type: e2e
status: draft
created: 2026-06-14
---

# TC-LV-255: Reports cross-browser + responsive 360px–1920px

## 1. Test Objective
Verify the reports UI (landing grid, filter sidebar, results table, charts, export/print) renders and functions across supported browsers (Chrome, Edge, Firefox, Safari) and is responsive from 360px to 1920px, with the documented mobile simplifications.

## 2. Related Requirements
- User Story: US-LV-012
- UI/UX: §8 (mobile table/list view, simplified charts, tap-for-details)
- Non-Functional Requirements: NFR-4, NFR-5

## 3. Preconditions
- Tenant "acme"; HR authenticated; a report with chart + table available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | cross-browser |
| Viewports | 360, 768, 1280, 1920 px | responsive |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the reports landing + a report in each browser | The grid, filter sidebar, table, and charts render correctly with no layout breakage or console errors. |
| 2 | Resize to 1920px and 1280px | Full desktop layout: sidebar + results + charts side-by-side; charts render with hover/tooltips. |
| 3 | Resize to 768px and 360px | Layout collapses gracefully: table becomes scrollable/list view, charts simplify (tap-for-details, no hover tooltips per §8); export/print remain reachable. |
| 4 | Exercise export + a filter at 360px | Filters apply and export downloads correctly on mobile widths. |

## 6. Postconditions
- Reports render and function across supported browsers and from 360px to 1920px with the documented mobile behavior.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
