---
id: TC-AUTH-038
user_story: US-AUTH-005
module: Authentication
priority: medium
type: accessibility
status: draft
created: 2026-06-03
---

# TC-AUTH-038: Accessibility of MFA enrollment and challenge UI

## 1. Test Objective
Verify that the MFA enrollment flow (QR code, 6-digit input, recovery code display) and the MFA login challenge UI meet WCAG 2.1 AA accessibility standards, including keyboard navigation, screen reader compatibility, focus management, and color contrast.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-2, AC-3, AC-4, AC-7
- UI/UX Notes: Section 8 of US-AUTH-005

## 3. Preconditions
- The Angular frontend is deployed and accessible via a browser.
- Screen reader software is available (NVDA on Windows, VoiceOver on macOS).
- axe-core browser extension or axe-core CLI is installed for automated checks.
- Test user account is available for both enrollment and challenge flows.
- Browser zoom is at 100%.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browser | Chrome 126+ (primary), Firefox 128+, Edge 126+, Safari 18+ | Cross-browser |
| Screen Reader | NVDA 2025+ / VoiceOver | Platform-appropriate |
| axe-core | v4.9+ | Automated WCAG checker |
| Viewport sizes | 360px (mobile), 768px (tablet), 1920px (desktop) | Responsive |
| Contrast ratio target | 4.5:1 (normal text), 3:1 (large text) | WCAG AA |

### MFA UI Components Under Test
| Component | Location | Key Requirements |
|-----------|----------|-----------------|
| QR code card | Enrollment step 1 | Alt text, text fallback ("copy secret" link) |
| 6-digit code input | Enrollment step 2 + login challenge | Label, aria-label, autocomplete="one-time-code", autofocus |
| Recovery code list | Enrollment step 3 | Readable by screen reader, copyable, downloadable |
| Success/failure alerts | All steps | role="alert" or aria-live="polite", announced by SR |
| "Use recovery code" link | Login challenge | Discoverable via keyboard, descriptive label |
| "Back to login" link | Login challenge | Keyboard accessible |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the MFA enrollment page using only the keyboard (Tab, Enter, Escape) | All interactive elements are reachable and activatable via keyboard. Focus order follows visual order. |
| 2 | Verify the QR code image has meaningful alt text | Alt text such as "QR code for MFA setup -- scan with your authenticator app" is present. |
| 3 | Verify a text-based fallback is available for the QR code | A "Copy secret key" link/button is present and functional for users who cannot scan. |
| 4 | Focus the 6-digit TOTP code input field | Field has a visible `<label>` element (e.g., "Verification code") associated via `for`/`id` or `aria-label`. |
| 5 | Inspect the input field's HTML attributes | `autocomplete="one-time-code"` is set, `inputmode="numeric"` is set, `maxlength="6"` or equivalent validation is present. |
| 6 | Type 6 digits into the TOTP input | Auto-submit triggers (or a submit button is clearly available). Focus moves to the result area. |
| 7 | On successful verification, check for a success announcement | An element with `role="alert"` or `aria-live="polite"` announces "MFA enabled successfully" or equivalent. Screen reader (NVDA/VoiceOver) reads it aloud. |
| 8 | On failed verification (wrong code), check for an error announcement | An element with `role="alert"` announces the error message. The input field receives focus for correction. The error text has 4.5:1 contrast ratio against its background. |
| 9 | Navigate to the recovery code display (enrollment step 3) | Recovery codes are presented in a list or grid. Each code is readable by screen reader. |
| 10 | Verify "Copy codes" and "Download codes" buttons are accessible | Buttons have descriptive labels, are keyboard-focusable, and announce their action on activation. |
| 11 | Verify recovery code warning message is announced | A warning such as "Save these codes -- they will not be shown again" has `role="alert"` or is otherwise announced. |
| 12 | Navigate to the MFA login challenge page (after password entry) | Focus is automatically placed on the 6-digit input or the MFA challenge card. |
| 13 | Verify "Use a recovery code" link is keyboard-accessible and has a descriptive label | Link text is not just "click here" but something like "Use a recovery code instead." |
| 14 | Verify "Back to login" link is present and keyboard-accessible | Link returns to the login form. |
| 15 | Run axe-core automated scan on the MFA enrollment page | Zero critical or serious WCAG 2.1 AA violations. Minor/moderate issues are logged for review. |
| 16 | Run axe-core automated scan on the MFA challenge page | Zero critical or serious WCAG 2.1 AA violations. |
| 17 | Test at 360px viewport width | All MFA UI elements are visible, QR code is appropriately sized, no horizontal scrolling required for the input fields. |
| 18 | Test at 200% browser zoom | Layout does not break, text remains readable, input fields remain usable. |
| 19 | Verify color contrast of all text elements using a contrast checker | All text meets 4.5:1 ratio (normal) or 3:1 (large/18pt+). Error states do not rely solely on color. |
| 20 | Verify focus indicator is visible on all interactive elements | Focus ring or outline is clearly visible with sufficient contrast against the background. |

## 6. Postconditions
- All MFA UI components meet WCAG 2.1 AA compliance.
- axe-core scan reports are saved for audit trail.
- Any identified accessibility issues are logged as defects with severity.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
