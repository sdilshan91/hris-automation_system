---
id: TC-LV-019
user_story: US-LV-001
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-LV-019: WCAG 2.1 AA accessibility for leave type configuration page

## 1. Test Objective
Verify that the leave type configuration page (list and create/edit form) meets WCAG 2.1 AA accessibility standards: keyboard navigation, screen reader compatibility, color contrast, and form labeling.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Leave Types configuration page is accessible.
- Screen reader software available (NVDA, VoiceOver, or JAWS).
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Parameter | Value | Notes |
|-----------|-------|-------|
| WCAG Level | AA | Minimum compliance |
| Contrast Ratio (normal text) | >= 4.5:1 | WCAG 1.4.3 |
| Contrast Ratio (large text) | >= 3:1 | WCAG 1.4.3 |
| Focus Indicator | Visible | WCAG 2.4.7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types page using only keyboard (Tab, Enter) | Page loads, focus starts at a logical point (skip-to-content or main heading). |
| 2 | Tab through the leave types list | Each row/card is focusable. Active/Inactive toggle, Edit, and action buttons receive focus with visible focus indicator. |
| 3 | Press Enter on "Add Leave Type" | Slide-over/modal opens and focus is trapped within it. |
| 4 | Tab through all form fields | Each field receives focus in logical order. Labels are associated with inputs (via `for`/`id` or `aria-labelledby`). Required fields have `aria-required="true"`. |
| 5 | Verify color contrast with an automated tool (axe, Lighthouse) | All text meets WCAG 1.4.3 contrast ratios. |
| 6 | Activate screen reader and navigate the form | Screen reader announces: field labels, required status, error messages, toggle states (on/off), dropdown options. Color tag is not conveyed solely through color (has text label). |
| 7 | Submit form with validation errors | Error messages are announced by screen reader via `aria-live` or focus shift. Each error is linked to its field. |
| 8 | Close the slide-over (Escape key) | Focus returns to the "Add Leave Type" button (focus management). |
| 9 | Verify that the inline active/inactive toggle is accessible | Toggle has `role="switch"`, `aria-checked`, and announces state change on activation. |

## 6. Postconditions
- Page passes WCAG 2.1 AA automated checks (no critical violations).
- All interactive elements are keyboard-accessible and screen-reader-compatible.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
