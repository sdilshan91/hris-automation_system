---
id: TC-LV-086
user_story: US-LV-004
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-LV-086: Queue and detail panel are usable on mobile 360px+ and WCAG 2.1 AA accessible with non-color cues

## 1. Test Objective
Verify that the pending queue and the slide-in detail panel are fully usable on mobile viewports from 360px, are keyboard- and screen-reader-navigable, and that the color-coded balance pills and overdue highlights carry non-color cues (icon/label/text), meeting WCAG 2.1 AA.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8 (compact card view on mobile, color-coded pills, overdue border)

## 3. Preconditions
- Tenant "acme" is active; a manager with `Leave.Approve.Team` is authenticated.
- The pending queue has rows including a green/yellow/red balance pill and at least one overdue request.
- Tools available: axe-core/Lighthouse, a screen reader (NVDA/VoiceOver), keyboard-only input, responsive viewport emulation.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 414px, 768px, 1024px, 1920px | Responsive range |
| Standard | WCAG 2.1 AA | -- |
| Contrast minimum | 4.5:1 text, 3:1 large text/UI | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the queue at 360px width | Layout reflows to a compact card view; no horizontal scroll; all row data (employee, type, dates, days, balance) remains readable; filter bar collapses to an accessible control. |
| 2 | Open a request's detail panel at 360px | The panel renders full-screen/near-full-width; attachments, balance, and content are reachable and scrollable; a visible close control exists. |
| 3 | Navigate the queue and panel with the keyboard only (Tab/Shift+Tab/Enter/Esc/arrows) | Logical focus order through filters, rows, row-open, and panel controls; visible focus indicator on every control; Esc closes the panel and returns focus to the originating row. |
| 4 | Traverse with a screen reader | Each row exposes an accessible name summarizing employee/type/dates/balance; the balance pill's band and the overdue state are announced as text, not implied by color; the panel is announced on open (focus moved/aria). |
| 5 | Verify balance pills and overdue highlight are not color-only | Each pill includes a numeric value/label; overdue rows include an "Overdue" icon/label in addition to the red border. |
| 6 | Run axe-core / Lighthouse on queue and panel | Zero critical/serious violations; contrast meets 4.5:1 (text) and 3:1 (UI/large text). |
| 7 | Verify quick-action buttons (Approve/Reject) are reachable | On mobile they are operable (not hover-only); keyboard-focusable with accessible names. |

## 6. Postconditions
- Queue and detail panel are usable from 360px upward, keyboard/SR accessible, with non-color cues.
- No critical/serious WCAG 2.1 AA violations remain.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
