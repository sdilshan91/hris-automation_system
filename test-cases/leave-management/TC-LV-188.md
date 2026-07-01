---
id: TC-LV-188
user_story: US-LV-009
module: Leave Management
priority: high
type: accessibility
status: fail
created: 2026-06-14
exec_note: "FAIL 2026-07-01 — /leave/team-calendar reached via inject-axe + soft-nav as employee@acme.test. axe (wcag2a/2aa/21a/21aa) on main: serious color-contrast (3 nodes) on the view-toggle buttons (button[data-test=view-week] etc.) → BUG-096 class. Calendar grid renders Month/Week/List toggles, Today, day cells, a 'Holiday' text marker (non-color cue present for holidays). PARTIAL data: acme shows 'No team leave for July 2026' so the per-leave-type color blocks (the central non-color-cue/aria-label arm of this TC) do not render — that arm not fully verifiable without populated team leave for the viewed month. Fail driven by the contrast defect on always-present controls."

# TC-LV-188: Calendar accessibility -- keyboard, screen reader, and non-color cues; usable at 360px+ (WCAG 2.1 AA, AC-4)

## 1. Test Objective
Verify the Team Leave Calendar meets WCAG 2.1 AA: fully keyboard navigable, screen-reader friendly, color-coded leave blocks carry non-color cues / text labels (so color is never the sole indicator), and the view remains usable from 360px up.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8 (color legend, today indicator)

## 3. Preconditions
- Manager "Maya" authenticated; calendar populated with multiple leave types (so color differentiation is testable).
- Keyboard-only operation and a screen reader (NVDA/VoiceOver) available; viewport tested at 360px and up.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Standard | WCAG 2.1 AA | contrast >= 4.5:1 text / 3:1 UI |
| Non-color cue | text label / pattern per leave type & status | color not sole indicator |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate the calendar (view toggle, filters, day cells, leave blocks) using Tab/Shift+Tab/arrow/Enter only | All interactive elements are reachable and operable by keyboard with a visible focus ring; no keyboard trap. |
| 2 | Inspect leave blocks with a screen reader | Each block exposes an accessible name (employee, leave type or "on leave", status, dates) via aria-label; the half-day and pending states are conveyed in text, not color alone. |
| 3 | Verify non-color cues and contrast | Leave-type and status are distinguishable by text/pattern as well as color; block/legend text meets >= 4.5:1 contrast; today/holiday cues are not color-only. |
| 4 | Resize from 360px upward | Layout reflows (list at 360px) without loss of content/function; no horizontal scroll trap; tap targets >= 44px. |

## 6. Postconditions
- Calendar is keyboard- and screen-reader-accessible with non-color cues and is usable from 360px up (WCAG 2.1 AA).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
