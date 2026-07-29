---
id: TC-AUTH-136
user_story: US-AUTH-016
module: Authentication
priority: medium
type: accessibility
status: blocked
created: 2026-07-24
---

# TC-AUTH-136: Accessibility (WCAG 2.1 AA) of the SSO Enforcement sub-section and the admin-consent onboarding wizard

## 1. Test Objective
Verify the new enforcement UI (Security > SSO > Enforcement: Optional/SSO-only selector, break-glass-admin picker, guarded lockout-risk confirmation dialog) and the multi-step admin-consent onboarding wizard (grant-consent -> confirm Directory ID -> review allow-list -> optional test login -> enable SSO) meet WCAG 2.1 AA: full keyboard operability, programmatic labels/roles, focus management in the modal dialog and across wizard steps, error/status announcement to assistive tech, and sufficient contrast. The high-risk lockout warning must be conveyed by more than color.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-1 (enforcement UI), AC-3 (guarded confirmation), AC-4 (consent wizard)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; the enforcement sub-section and onboarding wizard are reachable by a tenant admin.
- @axe-core/playwright available; a screen reader (NVDA/VoiceOver) for manual verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Contrast threshold | 4.5:1 text / 3:1 UI | WCAG AA |
| Tools | @axe-core/playwright + manual SR | Automated + manual |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Tab through the Enforcement sub-section (Optional/SSO-only selector, break-glass-admin picker, Save). | Every control is keyboard reachable/operable in a logical order with a visible focus indicator; the break-glass picker (combobox/list) is fully operable without a mouse and announces its options. |
| 2 | Trigger the guarded "enable SSO-only" confirmation dialog. | Focus moves into the modal and is trapped there; the dialog has `role="dialog"`/accessible name; Esc/Cancel returns focus to the trigger. The lockout-risk warning is announced and not conveyed by color alone. |
| 3 | Attempt SSO-only with no break-glass admin (AC-3 block). | The blocking error is programmatically associated (aria-describedby / aria-invalid) and announced by the screen reader -- not color-only. |
| 4 | Navigate the onboarding wizard steps with keyboard + screen reader. | Each step exposes its position/name (e.g. step 2 of 5), the "Grant admin consent" button and captured-Directory-ID confirmation are labelled and announced; status changes (consent pending/completed/failed) are announced via a live region. |
| 5 | Run an automated axe-core scan on the enforcement sub-section, the confirmation dialog, and each wizard step. | No serious/critical WCAG 2.1 AA violations; text and control contrast meet thresholds in each state, including the disabled/blocked SSO-only control. |

## 6. Postconditions
- The enforcement UI and onboarding wizard are keyboard-operable, screen-reader friendly, and contrast-compliant, including the high-risk confirmation dialog.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
