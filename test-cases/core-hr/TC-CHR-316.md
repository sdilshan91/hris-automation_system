---
id: TC-CHR-316
user_story: US-CHR-012
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-CHR-316: WCAG 2.1 AA accessibility for custom fields management page

## 1. Test Objective
Verify that the Custom Fields management page, creation/edit forms, and custom field rendering on employee forms meet WCAG 2.1 AA accessibility standards including keyboard navigation, screen reader compatibility, color contrast, and proper ARIA labeling.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with custom fields defined.
- Tenant Admin is authenticated.
- Screen reader software (NVDA/JAWS) available for testing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport | 1920px, 360px | Desktop and mobile |
| Custom Fields | 3 defined fields | Various types |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Custom Fields page using keyboard only (Tab, Enter, Escape). | All interactive elements (buttons, toggles, field cards) are reachable via Tab. Focus indicators are visible with sufficient contrast. |
| 2 | Open the "Add Custom Field" form using keyboard. | The form opens and focus moves to the first input. Escape closes the form. |
| 3 | Navigate the field type selector using keyboard. | Type options are navigable with arrow keys or Tab. Selection is confirmed with Enter/Space. |
| 4 | Use a screen reader to navigate the custom fields list. | Each field card announces: field name, field type, required/optional, usage count. ARIA labels are correct. |
| 5 | Use a screen reader on the dropdown option tag input. | The screen reader announces each chip, removal buttons, and the input for adding new options. |
| 6 | Verify color contrast of all text, labels, status badges, and toggle buttons. | All text meets WCAG AA minimum contrast ratio (4.5:1 for normal text, 3:1 for large text). |
| 7 | Verify custom field rendering on employee forms with screen reader. | Custom fields are announced with their label, type, required status, and current value. |
| 8 | Use the reorder arrow buttons via keyboard on mobile viewport. | Arrow buttons are focusable and operable via Enter/Space. |

## 6. Postconditions
- The custom fields management page and form rendering meet WCAG 2.1 AA standards.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
