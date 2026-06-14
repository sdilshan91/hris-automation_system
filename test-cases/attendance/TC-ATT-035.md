---
id: TC-ATT-035
user_story: US-ATT-003
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-035: Regularization drawer/form is accessible and responsive -- WCAG 2.1 AA, 360px full-screen mobile, live reason char-count (NFR-4 / UI notes)

## 1. Test Objective
Verify the regularization form meets WCAG 2.1 AA and the story's UI/UX notes (S8): the Notion-style drawer slides in from the right on desktop and becomes a FULL-SCREEN form on mobile to maximize input area; it is reachable and operable by keyboard with a visible focus indicator and a logical focus order (including focus trapped within the open drawer and returned on close); the reason field shows a LIVE character count and highlights when below the 10-character minimum, with the count/validation announced to screen readers; the date/time inputs and the approval-chain preview are labeled and announced; and the entire form is usable on a 360px viewport without horizontal scroll, with >= 48px touch targets.

## 2. Related Requirements
- User Story: US-ATT-003
- Non-Functional Requirements: NFR-4 (accessible & responsive on mobile, 360px minimum)
- UI/UX Notes (S8): right-slide drawer, full-screen on mobile, live reason char-count with below-minimum highlight, "Pending" status pill, approval-chain preview

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`, on the attendance history page with at least one regularizable date.
- Test tools: keyboard-only navigation, a screen reader (NVDA/VoiceOver), an automated a11y checker (axe), and a contrast checker.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | Mobile to desktop |
| Touch target min | 48 x 48 px | WCAG 2.5.5 |
| Contrast min | 4.5:1 text / 3:1 large text & UI | WCAG 1.4.3 / 1.4.11 |
| Reason minimum | 10 characters | Live count + below-min highlight |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | At 360px, activate "Request Regularization" for a date | The form opens FULL-SCREEN on mobile (not a cramped side drawer); all fields and the submit button are visible without horizontal scroll; controls are >= 48x48px. |
| 2 | On desktop (1280px), open the form | A Notion-style drawer slides in from the right; focus moves into the drawer and is trapped while open; a visible focus indicator is present. |
| 3 | Tab through date, type, time(s), reason, and submit; close with Esc | Logical tab order; Esc closes the drawer and returns focus to the triggering "Request Regularization" control. |
| 4 | Type into the reason field with a screen reader active | The live character count updates and is announced (e.g., via an ARIA live region); when below 10 chars, the field is marked invalid (aria-invalid + a programmatic error message), not by color alone. |
| 5 | Inspect the date/time inputs and the approval-chain preview with a screen reader | Each input has an associated label; the approval chain ("will be approved by Pat Kim") is announced as text, not conveyed only visually. |
| 6 | Submit and observe the "Pending" status pill | The pill conveys state by text/icon (not color alone) and meets contrast minimums; the status change is announced. |
| 7 | Run the automated a11y checker on the open form | Zero critical/serious violations; contrast minimums met for all controls and the char-count text. |
| 8 | Repeat focus/contrast/visibility at 768/1280/1920px and across Chrome, Edge, Firefox, Safari | The form remains accessible, correctly sized, and fully visible across breakpoints and the latest 2 versions of each browser. |

## 6. Postconditions
- No WCAG 2.1 AA blocking violations on the regularization form.
- The form is operable by keyboard and screen reader, full-screen at 360px, with a live, announced reason char-count, across supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
