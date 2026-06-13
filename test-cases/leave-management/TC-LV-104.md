---
id: TC-LV-104
user_story: US-LV-005
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-LV-104: Approve/Reject detail-panel actions are usable on mobile 360px+ and meet WCAG 2.1 AA (keyboard, screen reader, labeled mandatory-reason field)

## 1. Test Objective
Verify that the Approve/Reject actions in the request detail panel are fully responsive (full-width bottom action buttons on mobile 360px+) and meet WCAG 2.1 AA: operable by keyboard alone, announced by a screen reader, the mandatory rejection-reason textarea is properly labeled and marked required, and its validation error is programmatically announced.

## 2. Related Requirements
- User Story: US-LV-005
- UI/UX Notes (Section 8): prominent Approve/Reject buttons; mandatory reason textarea; full-width buttons on mobile
- Business Rules: BR-2 (mandatory reason)

## 3. Preconditions
- Tenant "acme" is active; Manager authenticated with `Leave.Approve.Team`.
- A pending request detail panel is open.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px | Responsive checks |
| Tools | Keyboard only, screen reader (NVDA/VoiceOver), axe | A11y checks |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Render the detail panel at 360px | Approve (green/checkmark) and Reject (red/X) buttons are full-width at the bottom and usable without horizontal scroll (Section 8). |
| 2 | Navigate the panel with Tab/Shift+Tab only | Focus reaches Approve, Reject, the comment/reason textarea, and the confirm/cancel controls in a logical order with a visible focus indicator. |
| 3 | Activate Reject via keyboard (Enter/Space) | The rejection-reason textarea appears and receives focus; it has an associated `<label>` and `aria-required="true"`. |
| 4 | Submit reject with an empty reason | An inline validation error is shown AND announced to the screen reader (e.g., `aria-describedby` / `role="alert"` / `aria-invalid`). |
| 5 | Run an automated axe scan on the panel | No critical/serious WCAG 2.1 AA violations; color contrast of the Approve/Reject buttons meets >= 4.5:1 (or 3:1 for large text/UI components). |
| 6 | Confirm a successful action with a screen reader | The success toast "Leave request approved/rejected for [Employee Name]" is announced via a live region. |

## 6. Postconditions
- Approve/Reject flow is operable, perceivable, and responsive at 360px+; mandatory-reason field labeled and errors announced.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
