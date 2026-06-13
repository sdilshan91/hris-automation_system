---
id: TC-LV-065
user_story: US-LV-003
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-LV-065: Leave application form is usable on mobile 360px+ and WCAG 2.1 AA accessible

## 1. Test Objective
Verify that the leave application form is fully usable on mobile viewports from 360px wide, with touch-friendly date pickers, a sticky submit button, and that it meets WCAG 2.1 AA: keyboard-only navigation, screen-reader labeling, focus order, and color contrast.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8 (mobile full-screen form, sticky submit)

## 3. Preconditions
- Tenant "acme" is active; an employee with `Leave.Apply` is authenticated.
- The leave application page is reachable.
- Testing tools available: axe-core/Lighthouse, a screen reader (NVDA/VoiceOver), keyboard-only input.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 414px, 768px, 1024px, 1920px | Responsive range |
| Standard | WCAG 2.1 AA | -- |
| Contrast minimum | 4.5:1 text, 3:1 large text/UI | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the leave application form at 360px width | Layout reflows to a single column full-screen form; no horizontal scroll; all fields reachable; submit button is sticky at the bottom. |
| 2 | Open the date picker on a touch device at 360px | The calendar is touch-friendly (tap targets >= 44x44px); date selection works without a mouse; AM/PM half-day selector is reachable. |
| 3 | Navigate the entire form with the keyboard only (Tab/Shift+Tab/Enter/Space/arrow keys) | Logical focus order through leave type, dates, half-day toggle, reason, attachment, submit; visible focus indicator on every control; date picker is operable via keyboard. |
| 4 | Use a screen reader to traverse the form | Every input has an accessible name/label; the balance panel and "days calculated" chip are announced; validation errors are announced (aria-live) and associated with their fields. |
| 5 | Run axe-core / Lighthouse accessibility audit | Zero critical/serious violations; color contrast meets 4.5:1 (text) and 3:1 (UI components/large text). |
| 6 | Verify error and required-field states are not conveyed by color alone | Errors include text/icon, not just red color. |
| 7 | Verify at 1920px the form remains usable and centered | No stretched/broken layout; controls remain accessible. |

## 6. Postconditions
- The form is usable from 360px upward with touch-friendly controls.
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
