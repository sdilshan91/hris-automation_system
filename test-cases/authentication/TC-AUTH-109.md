---
id: TC-AUTH-109
user_story: US-AUTH-010
module: Authentication
priority: medium
type: accessibility
status: draft
created: 2026-06-11
---

# TC-AUTH-109: Lockout error banner and admin lockout UI meet WCAG 2.1 AA accessibility standards

## 1. Test Objective
Verify that the lockout error messages on the login form and the admin lockout management UI are accessible: keyboard navigable, screen reader compatible, sufficient color contrast on the error banner and "Locked" badge, and appropriate ARIA roles/labels.

## 2. Related Requirements
- User Story: US-AUTH-010
- UI/UX Notes: Section 8
- WCAG 2.1 AA compliance

## 3. Preconditions
- User `alice@acme.com` is locked.
- The Angular frontend is running.
- Screen reader software (e.g., NVDA, VoiceOver) is available.
- Browser accessibility audit tool (e.g., axe, Lighthouse) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Locked user | alice@acme.com | Triggers lockout banner |
| Browser | Chrome, Firefox, Edge, Safari | Cross-browser |
| Viewport widths | 360px, 768px, 1920px | Responsive |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the login page. Attempt login as `alice@acme.com` to trigger the lockout banner. | Lockout error banner is displayed. |
| 2 | Run an accessibility audit (axe/Lighthouse) on the login page with the lockout banner visible. | No WCAG 2.1 AA violations related to the lockout banner. |
| 3 | Verify the lockout banner has `role="alert"` or equivalent ARIA live region so screen readers announce it automatically. | Screen reader announces the lockout message when it appears. |
| 4 | Verify the lockout banner text has a color contrast ratio of at least 4.5:1 against its background. | Contrast ratio meets WCAG AA threshold. |
| 5 | Verify the "Locked" badge (red indicator) in the admin user management page has sufficient contrast and an accessible label. | Badge has `aria-label` or visible text; contrast ratio >= 3:1 for non-text elements. |
| 6 | Navigate the admin user management page using only the keyboard (Tab, Enter, Space). | The "Unlock" button is reachable and activatable via keyboard. |
| 7 | Verify the lockout banner is displayed correctly on mobile viewport (360px). | Banner is visible, readable, and does not overflow the screen. |
| 8 | Verify the security settings form fields for lockout policy configuration have proper labels and are keyboard accessible. | All form fields have associated `<label>` elements and are focusable via Tab. |

## 6. Postconditions
- Lockout-related UI elements meet WCAG 2.1 AA standards.
- No accessibility barriers for lockout messaging or admin lockout management.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
