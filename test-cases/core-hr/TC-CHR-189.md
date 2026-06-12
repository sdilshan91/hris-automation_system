---
id: TC-CHR-189
user_story: US-CHR-007
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-189: Locations management page meets WCAG 2.1 AA accessibility standards

## 1. Test Objective
Verify that the Locations management page (list view and add/edit form) meets WCAG 2.1 Level AA accessibility standards including keyboard navigation, screen reader compatibility, color contrast, and focus management. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- At least 3 locations exist in tenant "acme" (for list navigation testing).
- Screen reader software is available (e.g., NVDA, VoiceOver).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Screen Reader | NVDA or VoiceOver | For assistive technology testing |
| Contrast Tool | axe DevTools or Lighthouse | For contrast ratio verification |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page using keyboard only (Tab key) | All interactive elements (buttons, rows, actions) are reachable via Tab. Focus order is logical (top to bottom, left to right). |
| 2 | Activate "Add Location" button using Enter/Space key | The add location form opens. Focus moves to the first form field. |
| 3 | Navigate through all form fields using Tab key | All form fields (name, address, time zone dropdown, phone, status toggle) are reachable. Tab order follows visual order. |
| 4 | Open and navigate the Time Zone searchable dropdown using keyboard | Dropdown opens on Enter/Space. Arrow keys navigate options. Enter selects. Escape closes. |
| 5 | Submit the form using Enter key | Form submits. Success feedback is announced by the screen reader. |
| 6 | Activate the screen reader and navigate the locations list | Each location card/row is announced with its name, city, country, time zone, employee count, and status. Table semantics (if table) or list semantics (if cards) are correct. |
| 7 | Verify all form labels are programmatically associated with inputs | Every `<input>`, `<select>`, and toggle has an associated `<label>` or `aria-label`. Required fields are indicated with `aria-required="true"`. |
| 8 | Verify error messages are announced | When validation errors appear (e.g., missing required field), the screen reader announces the error. Errors are linked to fields via `aria-describedby`. |
| 9 | Run axe DevTools or Lighthouse accessibility audit on the Locations page | No critical or serious accessibility violations. Color contrast ratio is at least 4.5:1 for normal text and 3:1 for large text. |
| 10 | Verify the Status toggle is accessible | Toggle has a visible label, keyboard-operable (Space key), and screen reader announces the on/off state. |

## 6. Postconditions
- No accessibility violations detected at WCAG 2.1 AA level.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
