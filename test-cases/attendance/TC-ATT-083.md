---
id: TC-ATT-083
user_story: US-ATT-006
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-083: Overtime UI -- unified approval-hub overtime tab, color-coded status tags, collapsible daily OT card, weekly progress bar, monthly report table accessible & responsive (WCAG 2.1 AA, 360px)

## 1. Test Objective
Verify the UI/UX Section 8 + accessibility expectations for overtime: the manager approval hub exposes a separate overtime tab/filter alongside regularization; status tags (amber Pending, green Approved, red Rejected, gray Unapproved) convey state by text, not color alone; the employee daily attendance card shows a distinct, collapsible overtime detail section (hours + multiplier); a weekly-overtime progress bar shows approach to the maximum; the pre-approval form (date, expected hours, reason) is labeled; the monthly overtime report is a sortable table with an export button; everything is keyboard- and screen-reader-operable and usable at 360px.

## 2. Related Requirements
- User Story: US-ATT-006
- UI/UX Notes: Section 8 (distinct daily overtime section with hours + multiplier; unified approval hub with overtime tab/filter; color-coded tags amber/green/red/gray; sortable monthly report table + export; pre-approval form; collapsible mobile detail; weekly progress bar)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme"; manager and employee accounts with overtime data spanning Pending/Approved/Rejected/Unapproved statuses and a week approaching the cap.
- Tested on Chrome + a screen reader (NVDA/VoiceOver) and an automated axe scan; viewports 360/768/1280/1920px.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |
| Components | OT approval tab, status tags, daily OT card, weekly progress bar, pre-approval form, monthly report table | |
| Statuses shown | Pending, Approved, Rejected, Unapproved | color + text |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an axe (WCAG 2.1 AA) scan on the approval hub overtime tab, daily card, weekly progress bar, pre-approval form, and monthly report | No critical/serious violations; text/control/tag contrast >= 4.5:1; the progress bar exposes its value via ARIA, not color alone. |
| 2 | Operate the unified approval hub by keyboard | The overtime tab/filter is reachable and announced; switching between the regularization and overtime queues is keyboard-operable with visible focus; the approve/reject controls and the reason/comment field are labeled and announce the mandatory-reason rule. |
| 3 | Verify status tags | Each tag (amber Pending / green Approved / red Rejected / gray Unapproved) conveys state through text/icon, not color alone, so it is distinguishable by color-blind and screen-reader users. |
| 4 | Operate the employee daily attendance card | The overtime detail section is a distinct, keyboard-toggleable collapsible region announcing expanded/collapsed state and exposing hours + multiplier. |
| 5 | Inspect the weekly progress bar | Conveyed as a labeled progressbar with current/max values announced; approaching-cap state is not color-only. |
| 6 | Operate the pre-approval form | Date, expected hours, and reason inputs are labeled, validated with announced errors, and keyboard-submittable. |
| 7 | Operate the monthly report table | Sortable column headers are keyboard-operable and announce sort state; the export button is labeled and reachable. |
| 8 | Resize to 360px | The approval hub, daily card (collapsible OT), progress bar, pre-approval form, and report remain usable without horizontal scroll; touch targets >= 44-48px; tables reflow to cards where needed. |
| 9 | Cross-browser smoke (Chrome, Edge, Firefox, Safari) | All overtime surfaces render and operate consistently. |

## 6. Postconditions
- The overtime UI meets WCAG 2.1 AA, is keyboard/screen-reader operable, conveys status without relying on color, and is fully usable at 360px across browsers.

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
- Reuses the approval-hub a11y patterns established for regularization (US-ATT-004 TC-ATT-050) and extends them to the overtime tab, daily collapsible OT, weekly progress bar, and monthly report.
