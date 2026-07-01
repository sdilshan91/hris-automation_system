---
id: TC-ATT-116
user_story: US-ATT-008
module: Attendance
priority: high
type: accessibility
status: blocked
exec_note: "S2 2026-07-01: BLOCKED (partial) — primary Late&Early report page (acme, manager persona, soft-nav) axe WCAG2.1AA scan CLEAN except 1 serious color-contrast on .user-email (sidebar chrome, known BUG-108/112 family, not report-specific). Full TC not executable: SR announcements are manual, 360px sub-scans + HR-only late-policy form (manager->/forbidden) + lateness-score view not covered this pass. No new report-specific finding."
created: 2026-06-14
---

# TC-ATT-116: Late/early UI -- daily-card late/early badges (text-not-color), report table conditional formatting, lateness-score indicator, late-policy form, all keyboard/screen-reader operable & usable at 360px (WCAG 2.1 AA)

## 1. Test Objective
Verify the §8 UI/UX + accessibility expectations for late/early tracking: the daily attendance card shows "Late by N min" / "Left N min early" badges that convey meaning by text/icon (not color alone); the manager late/early report renders as a Notion-style table with conditional formatting (amber rows for chronic lateness) that is still distinguishable without color; the monthly lateness-score / progress indicator ("2 of 3 allowed lates used"); and the HR late-policy form -- all keyboard-operable, screen-reader-announced, contrast-compliant, and usable at 360px including mobile attendance cards where badges are visible without expansion.

## 2. Related Requirements
- User Story: US-ATT-008
- UI/UX Notes: §8 (late/early badges with minutes, monthly late/early columns, conditional-formatted report table, monthly trend bar chart, mobile card badges, lateness-score indicator)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme" with seeded late/early records spanning on-time, late, early, and chronic-late employees; an active late_policy.
- Tested on Chrome + a screen reader (NVDA/VoiceOver) + an automated axe scan; viewports 360/768/1280/1920px.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |
| Components | daily card badge, monthly summary columns, late/early report table, lateness-score indicator, trend bar chart, late-policy form | §8 |
| Color states | amber late, red chronic-late, neutral on-time | + text/icon |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an axe (WCAG 2.1 AA) scan on the daily card, report, score indicator, and policy form | No critical/serious violations; text/control/badge contrast >= 4.5:1 (amber/red pills included). |
| 2 | Inspect the daily-card late/early badge | "Late by 12 min" / "Left 30 min early" convey state via TEXT (and icon), not color alone; the badge is announced by the screen reader with its minutes. |
| 3 | Operate the late/early report table by keyboard | Sortable headers and rows are keyboard-operable and announce sort/scope; conditional amber "chronic" rows expose that state via text/label/icon, not color alone. |
| 4 | Operate the lateness-score indicator | "2 of 3 allowed lates used this month" is announced as text (the progress bar has a text/ARIA alternative, not visual-only). |
| 5 | Operate the late-policy form | All fields (threshold, deduction days, period, notification + chronic toggles, is_active) have programmatic labels; validation errors are announced and associated with their field. |
| 6 | Verify the monthly trend bar chart | The per-employee late-arrival trend chart has a text/table/ARIA alternative (not chart-only). |
| 7 | Resize to 360px | Late/early badges are visible on the mobile attendance card WITHOUT requiring expansion (§8); the report reflows to a card layout; no horizontal scroll; touch targets >= 44-48px. |
| 8 | Cross-browser smoke (Chrome, Edge, Firefox, Safari) | All late/early surfaces render and operate consistently. |

## 6. Postconditions
- The late/early UI meets WCAG 2.1 AA, is keyboard/screen-reader operable, conveys status without relying on color, and is fully usable at 360px across browsers.

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
- Reuses the table/filter-chip + status-pill a11y patterns from the overtime report (US-ATT-006 TC-ATT-083) and the monthly summary (US-ATT-007 TC-ATT-099), extending them to the late/early badges, conditional-formatted report rows, lateness-score indicator, and the late-policy form. §8 specifically requires mobile badges visible without expansion (Step 7).
