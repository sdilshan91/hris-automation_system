---
id: TC-AUTH-125
user_story: US-AUTH-012
module: Authentication
priority: medium
type: accessibility
status: draft
created: 2026-07-24
---

# TC-AUTH-125: Accessibility (WCAG 2.1 AA) of the Single Sign-On settings card

## 1. Test Objective
Verify the new "Single Sign-On (Microsoft Entra ID)" settings card meets WCAG 2.1 AA: full keyboard operability of every control (enable toggle, multi-entry `tid`/domain inputs and their chips, default-role dropdown, JIT toggle, enforcement selector), programmatic labels and error association for screen readers, and sufficient color contrast including for the disabled/"upgrade your plan" state.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-1 (UI surface), AC-2 (disabled/upgrade state)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme" plan has `Sso = true` (for the enabled card); a second run against a `Sso = false` tenant for the disabled/upgrade state.
- axe-core (or equivalent) available for automated a11y scanning; a screen reader (NVDA/VoiceOver) for manual verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Contrast threshold | 4.5:1 text / 3:1 UI | WCAG AA |
| Tools | @axe-core/playwright + manual SR | Automated + manual |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Tab through the entire SSO card from the enable toggle to Save. | Every control is reachable and operable by keyboard in a logical order; a visible focus indicator is present on each; no keyboard trap (esp. in the multi-entry chip inputs). |
| 2 | Using a screen reader, focus each field. | Each control has an accessible name/label; the multi-entry inputs announce added/removed chips; the default-role dropdown announces its options. |
| 3 | Trigger an inline validation error (invalid `tid`). | The error is programmatically associated with its field (aria-describedby / aria-invalid) and announced by the screen reader -- not conveyed by color alone. |
| 4 | Run an automated axe-core scan on the enabled card. | No serious/critical WCAG 2.1 AA violations; text and control contrast meet thresholds. |
| 5 | Load the card in the `Sso = false` (disabled/"Available on higher plans") state and scan. | The disabled state and upgrade note meet contrast requirements and the disabled controls are correctly conveyed to assistive tech (not just visually greyed). |

## 6. Postconditions
- The SSO settings card is fully keyboard-operable, screen-reader friendly, and contrast-compliant in both enabled and disabled states.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
