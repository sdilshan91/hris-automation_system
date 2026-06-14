---
id: TC-ATT-011
user_story: US-ATT-001
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-011: Clock-in card is accessible and responsive — WCAG 2.1 AA, 360px, 48px touch target, keyboard and screen reader

## 1. Test Objective
Verify that the dashboard Clock-In card meets WCAG 2.1 AA: it is fully operable by keyboard, announced correctly by a screen reader, meets color-contrast minimums, presents a touch target of at least 48px, and remains usable and full-width on a 360px-wide mobile viewport (NFR-5).

## 2. Related Requirements
- User Story: US-ATT-001
- Non-Functional Requirements: NFR-5
- UI/UX Notes (S8): primary-action card, full-width on mobile, >= 48px touch target, success toast (not modal)

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, no open clock-in.
- Test tools available: keyboard-only navigation, a screen reader (NVDA/VoiceOver), and an automated a11y checker (axe) plus manual verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | Mobile to desktop |
| Touch target min | 48 x 48 px | UI/UX note + WCAG 2.5.5 |
| Contrast min | 4.5:1 text / 3:1 large text & UI | WCAG 1.4.3 / 1.4.11 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard at 360px width | The Clock-In card is full-width, not clipped, and the "Clock In" control is at least 48x48px and easily tappable. No horizontal scroll. |
| 2 | Navigate to the Clock-In button using Tab only | The button receives a visible focus indicator and is reachable in a logical tab order. |
| 3 | Activate the button with Enter and (separately) Space | Both keys trigger clock-in; the action is not mouse-only. |
| 4 | With a screen reader running, focus the button | The control's accessible name announces its purpose (e.g., "Clock In, button"); shift context (shift name, expected start) is also reachable/announced. |
| 5 | After clock-in, observe the success toast with a screen reader | The toast is announced via an ARIA live region (polite) so non-sighted users learn of success; it is not a focus-trapping modal. |
| 6 | If geolocation is required, check the permission/error messaging | Any error/inline message is programmatically associated with the control and announced, not conveyed by color alone. |
| 7 | Run the automated a11y checker on the dashboard | Zero critical/serious violations on the Clock-In card; text and UI components meet contrast minimums. |
| 8 | Repeat focus/contrast checks at 768/1280/1920px | Card and controls remain accessible and correctly sized across breakpoints. |

## 6. Postconditions
- No WCAG 2.1 AA blocking violations on the Clock-In card.
- Card is operable by keyboard and screen reader at all tested viewports.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
