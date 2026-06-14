---
id: TC-ATT-024
user_story: US-ATT-002
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-024: Clock-out button and summary card are accessible and responsive — WCAG 2.1 AA, 360px no-scroll, status pills (NFR-5 / UI notes)

## 1. Test Objective
Verify the dashboard clock-out experience meets WCAG 2.1 AA and the story's UI/UX notes: the "Clock Out" button is keyboard-operable with a visible focus indicator and >= 48px touch target; the fade-in summary card (clock-in, clock-out, total hours, overtime) is announced to screen readers via an ARIA live region; the Notion-style status pills (green "Complete", amber "Short Day", blue "Overtime") convey state by text/icon and not by color alone; and the clock-out button plus summary are fully visible on a 360px viewport without scrolling.

## 2. Related Requirements
- User Story: US-ATT-002
- Non-Functional Requirements: NFR-5 (timezone display correctness)
- UI/UX Notes (S8): warm-colored Clock Out button, fade-in summary card, status pills, mobile full visibility without scroll

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, with ONE open record (live elapsed timer running).
- Test tools: keyboard-only navigation, a screen reader (NVDA/VoiceOver), an automated a11y checker (axe), and a contrast checker.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | Mobile to desktop |
| Touch target min | 48 x 48 px | WCAG 2.5.5 |
| Contrast min | 4.5:1 text / 3:1 large text & UI | WCAG 1.4.3 / 1.4.11 |
| Status pills | Complete (green), Short Day (amber), Overtime (blue) | Must include text, not color-only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard at 360px width while clocked in | The "Clock Out" button AND (after action) the summary card are fully visible without horizontal or vertical scroll; the button is >= 48x48px. |
| 2 | Tab to the "Clock Out" button | Visible focus indicator; reachable in a logical tab order. |
| 3 | Activate with Enter and (separately) Space | Both keys trigger clock-out; not mouse-only. |
| 4 | With a screen reader, focus the button | Accessible name announces purpose (e.g., "Clock Out, button"); the warm color is not the only state cue. |
| 5 | After clock-out, observe the summary card with a screen reader | The summary (clock-in, clock-out, total hours, overtime) is announced via an ARIA live region (polite); the fade-in animation does not trap focus or block the announcement. |
| 6 | Inspect each status pill (Complete / Short Day / Overtime) | Each pill includes a text label (and/or icon), so state is not conveyed by color alone; pill text meets contrast minimums. |
| 7 | Run the automated a11y checker on the dashboard with the summary present | Zero critical/serious violations on the clock-out card and summary; contrast minimums met. |
| 8 | Repeat focus/contrast/visibility at 768/1280/1920px and across Chrome, Edge, Firefox, Safari | Button and summary remain accessible, correctly sized, and fully visible across breakpoints and the latest 2 versions of each browser. |

## 6. Postconditions
- No WCAG 2.1 AA blocking violations on the clock-out button or summary card.
- The flow is operable by keyboard and screen reader and fully visible at 360px across supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
