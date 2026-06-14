---
id: TC-ATT-141
user_story: US-ATT-010
module: Attendance
priority: medium
type: accessibility
status: draft
created: 2026-06-15
---

# TC-ATT-141: Accessibility & responsive -- KPI cards (stack at 360px), donut/bar/line charts (text alternative), live-board card layout + row-highlight, skeleton loaders; WCAG 2.1 AA, 360-1920px, cross-browser

## 1. Test Objective
Verify WCAG 2.1 AA accessibility and responsive behaviour for the US-ATT-010 dashboard + reports UI (§8, NFR-5): the KPI widget cards (which stack vertically on mobile), the donut/pie attendance-breakdown chart, the department horizontal-bar chart, the trend line charts, the live attendance board table (card layout on mobile, row-highlight animation on new clock-in), the report/scheduled-report forms, and the skeleton loaders -- all keyboard-operable, screen-reader friendly, sufficient contrast, charts conveyed by more than color, usable from 360px to 1920px across browsers.

## 2. Related Requirements
- User Story: US-ATT-010
- Non-Functional: NFR-5 (dashboard + reports fully responsive, usable on tablet/mobile)
- UI/UX: §8 (KPI cards, donut chart, live board with status pills + clock-in times + row-highlight, horizontal bar charts color-coded, smooth line charts with tooltips, report command palette/sidebar, export dropdown, scheduled-report form, mobile stacks KPI cards / swipeable charts / card-layout live board, skeleton loaders)
- Standard: WCAG 2.1 AA
- Cross-cutting a11y precedent: TC-ATT-128 (US-ATT-009), TC-ATT-116 (US-ATT-008), TC-ATT-099 (US-ATT-007)

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated; the dashboard + reports populated with data.
- Test with keyboard only, a screen reader (NVDA/VoiceOver), and an axe-core scan; viewports 360px and 1920px; Chrome, Edge, Firefox, Safari.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| viewport min | 360px | mobile responsive |
| viewport max | 1920px | desktop |
| components | KPI cards, donut chart, bar chart, line charts, live board, report form, export dropdown, scheduled-report form, skeleton loaders | §8 surfaces |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | KPI widget cards at 1920px then 360px | each KPI exposes its value + label to SR (not a bare number); at 360px the cards STACK vertically (§8) with no horizontal scroll/clipping; large numbers retain >= 4.5:1 contrast. |
| 2 | Donut/pie attendance-breakdown chart | has a text alternative / data table (Clocked In / On Leave / Absent / Pending values reachable by SR); segments not conveyed by color alone (labels/legend). |
| 3 | Department horizontal-bar chart | bars labeled with department + rate; the green/amber/red band is reinforced by the numeric value + text/icon (not color-only); keyboard-focusable bars or an equivalent table. |
| 4 | Trend line charts | tooltips reachable without hover (keyboard/SR), exact values available via a data-table alternative; lines distinguishable beyond color. |
| 5 | Live attendance board | proper table semantics (th/scope) at desktop; CARD layout at 360px (§8); status pills convey state by text not color; the new-clock-in row-highlight animation is not the sole indicator and respects prefers-reduced-motion; an aria-live update announces a status change. |
| 6 | Report command palette/sidebar + export dropdown | keyboard-operable, focus order logical, the "Export as CSV/Excel/PDF" dropdown is reachable + labeled; focus trap in any modal/drawer with Esc + focus-return. |
| 7 | Scheduled-report setup form | labeled inputs (report, frequency, filters, recipients, delivery time), errors announced; keyboard-complete. |
| 8 | Skeleton loaders | the loading state is announced (aria-busy / status) rather than presenting an empty silent page; focus is not lost when content swaps in. |
| 9 | axe-core scan on each surface | no critical/serious violations (labels, contrast, roles, names). |
| 10 | Cross-browser render (Chrome, Edge, Firefox, Safari) | consistent layout/behaviour across browsers. |

## 6. Postconditions
- The dashboard + reports UI meets WCAG 2.1 AA, is fully keyboard + screen-reader operable, charts have non-color/text alternatives, and it renders correctly from 360px to 1920px across browsers.

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
- Charts are rendered client-side (Chart.js / ngx-charts per §10); the accessible-alternative (data table / SR text) is the contract verified here, consistent with the module's chart-a11y precedent (US-ATT-007 TC-ATT-099 sparkline alternative). **Reported to caller.**
- The live-board row-highlight animation belongs to the SignalR/polling update path (TC-ATT-130); its a11y (reduced-motion + aria-live) is verified on the polling rendering and re-checked when SignalR lands. **Reported to caller.**
- Consistent with the one-a11y-TC-per-story precedent (TC-ATT-011/024/035/050/066/083/099/116/128).
