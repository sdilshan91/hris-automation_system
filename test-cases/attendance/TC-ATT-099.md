---
id: TC-ATT-099
user_story: US-ATT-007
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-099: Monthly summary UI -- sortable/filterable Notion-style table, month-year picker, color-coded cells (text-not-color), drill-down calendar grid, filter chips, summary banner, 360px card layout accessible & responsive (WCAG 2.1 AA)

## 1. Test Objective
Verify the UI/UX §8 + accessibility expectations for the monthly summary: a sortable/filterable Notion-style database table; a month-year picker (left/right arrows + dropdown); color-coded cells (red high-absence, amber high-late, green full-attendance) that convey meaning by text/icon, not color alone; per-employee sparkline; the drill-down calendar/grid with per-day color-coded statuses; Notion-style filter chips (department/location/shift); the summary banner (total employees, average attendance %, total LOP); and a mobile card layout with expandable detail -- all keyboard- and screen-reader-operable and usable at 360px.

## 2. Related Requirements
- User Story: US-ATT-007
- UI/UX Notes: §8 (sortable/filterable table, month-year picker, color-coded cells, sparkline, drill-down calendar grid, filter chips, export button, mobile card layout, summary banner)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme"; HR Officer with a generated month summary spanning full-attendance, high-absence, and high-late employees.
- Tested on Chrome + a screen reader (NVDA/VoiceOver) and an automated axe scan; viewports 360/768/1280/1920px.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |
| Components | summary table, month picker, color cells, sparkline, drill-down grid, filter chips, banner | §8 |
| Color states | red absent, amber late, green full | + text/icon |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an axe (WCAG 2.1 AA) scan on the summary page, drill-down, and filters | No critical/serious violations; text/control/cell contrast >= 4.5:1. |
| 2 | Operate the table by keyboard | Sortable column headers are keyboard-operable and announce sort state; rows are focusable and the drill-down opens via keyboard. |
| 3 | Verify color-coded cells | Red (high absent), amber (high late), green (full attendance) convey state through text/icon/label, not color alone, so color-blind and screen-reader users can distinguish them. |
| 4 | Operate the month-year picker | Left/right arrows and the dropdown are keyboard-reachable and announced; changing the month reloads the summary. |
| 5 | Operate the drill-down calendar grid | Each day cell exposes its status (present/absent/leave/holiday/weekly-off) via text/ARIA, not color alone; the grid is keyboard-navigable. |
| 6 | Operate the filter chips | Department/location/shift chips are reachable, announce their applied/removed state, and are keyboard-operable; the export button is labeled and reachable. |
| 7 | Verify the summary banner and sparkline | The banner (total employees, average attendance %, total LOP) is announced; the per-employee sparkline has a text/ARIA alternative (not chart-only). |
| 8 | Resize to 360px | Table reflows to the per-employee card layout with an expandable detail section; no horizontal scroll; touch targets >= 44-48px. |
| 9 | Cross-browser smoke (Chrome, Edge, Firefox, Safari) | All summary surfaces render and operate consistently. |

## 6. Postconditions
- The monthly summary UI meets WCAG 2.1 AA, is keyboard/screen-reader operable, conveys status without relying on color, and is fully usable at 360px across browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test

## 8. Notes
- Reuses the table/filter-chip and status-pill a11y patterns established for the overtime report (US-ATT-006 TC-ATT-083) and the approval hub (US-ATT-004 TC-ATT-050), extending them to the summary table, month-year picker, color-coded cells, sparkline, and drill-down calendar grid.
