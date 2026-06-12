---
id: TC-CHR-235
user_story: US-CHR-009
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-235: Status change form and timeline meet WCAG 2.1 AA accessibility standards

## 1. Test Objective
Verify that the status change modal/bottom sheet and the employment history timeline meet WCAG 2.1 AA standards, including keyboard navigation, screen reader compatibility, color contrast, and focus management. This validates general accessibility requirements.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-4 (responsive, implicitly covers a11y)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee "John Smith" (`emp-001-uuid`) exists with status `active` and has prior status change history.
- Screen reader (NVDA/VoiceOver) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | John Smith (emp-001-uuid) | Has status history |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the employee profile using keyboard only (Tab). | Focus reaches the "Change Status" button. Button has visible focus indicator. |
| 2 | Press Enter on the "Change Status" button. | Modal/bottom sheet opens. Focus is trapped within the modal. First focusable element (New Status dropdown) receives focus. |
| 3 | Navigate between form fields using Tab. | Focus moves through: New Status dropdown -> Reason textarea -> Effective Date picker -> Cancel button -> Confirm button. Tab order is logical. |
| 4 | Use arrow keys in the New Status dropdown. | Options can be navigated with up/down arrows. Selected option is announced by screen reader. |
| 5 | Press Escape. | Modal closes. Focus returns to the "Change Status" button. |
| 6 | Verify color contrast of the status badge and form labels. | All text meets WCAG AA minimum contrast ratio (4.5:1 for normal text, 3:1 for large text). Badge text on colored backgrounds meets contrast requirements. |
| 7 | Activate screen reader. Navigate to the status badge. | Screen reader announces the status (e.g., "Status: Suspended") and does not rely solely on color to convey the information. |
| 8 | Navigate to the employment history timeline with screen reader. | Each timeline entry is announced with: status, date, reason, and actor. Timeline entries are in a list or have appropriate ARIA roles. |
| 9 | Verify form error messages are announced. | When the form is submitted with missing fields, error messages are associated with their fields via `aria-describedby` or equivalent. Screen reader announces errors. |
| 10 | Check the confirmation dialog for accessibility. | Confirmation dialog is modal, focus is trapped, Cancel/Confirm buttons are clearly labeled, and the dialog purpose is announced to screen reader. |

## 6. Postconditions
- No data changes. This test verifies accessibility only.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
