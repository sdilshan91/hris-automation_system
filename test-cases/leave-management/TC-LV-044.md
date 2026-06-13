---
id: TC-LV-044
user_story: US-LV-002
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-13
---

# TC-LV-044: WCAG 2.1 AA accessibility for entitlement configuration page

## 1. Test Objective
Verify that the leave entitlement configuration page (matrix view, rule creation form, override form, and bulk assignment UI) meets WCAG 2.1 Level AA accessibility standards, including keyboard navigation, screen reader compatibility, and color contrast requirements.

## 2. Related Requirements
- User Story: US-LV-002
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with entitlement rules configured.
- A user with `Leave.Configure` permission is authenticated.
- Screen reader software available (NVDA, VoiceOver, or JAWS).
- Automated accessibility scanner (axe, Lighthouse) available.

## 4. Test Data
| Tool | Purpose |
|------|---------|
| axe / Lighthouse | Automated WCAG scan |
| Keyboard | Tab, Enter, Escape, Arrow keys navigation |
| NVDA / VoiceOver | Screen reader verification |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run axe accessibility scanner on the entitlement configuration page | Zero critical or serious WCAG 2.1 AA violations. |
| 2 | Navigate the entire page using only keyboard (Tab, Shift+Tab, Enter, Escape) | All interactive elements (buttons, form fields, matrix cells, dropdowns) are reachable and operable via keyboard. |
| 3 | Verify visible focus indicators on all focusable elements | Focus ring is clearly visible with sufficient contrast (3:1 minimum against adjacent colors). |
| 4 | Verify the entitlement matrix has proper table semantics | Matrix uses `<table>`, `<thead>`, `<th>` with `scope` attributes, or equivalent ARIA roles (`role="grid"`, `role="row"`, `role="columnheader"`). |
| 5 | Activate screen reader and navigate the matrix | Screen reader announces row headers (leave types), column headers (departments/levels), and cell values (entitlement days) correctly. |
| 6 | Verify "Add Rule" form has labeled inputs | All form fields have associated `<label>` elements or `aria-label` attributes. |
| 7 | Verify dropdown menus (leave type, department, job level) are keyboard accessible | Dropdowns open with Enter/Space, navigate with Arrow keys, select with Enter. |
| 8 | Verify error messages are announced by screen readers | Validation errors use `aria-invalid`, `aria-describedby`, or `role="alert"` to announce errors. |
| 9 | Verify color contrast of entitlement values in matrix cells | Text contrast ratio >= 4.5:1 for normal text, >= 3:1 for large text. |
| 10 | Verify that entitlement information is not conveyed by color alone | Override indicators, rule types, etc. use text/icon in addition to color. |

## 6. Postconditions
- The entitlement configuration page passes WCAG 2.1 AA automated and manual checks.
- All functionality is keyboard-operable and screen-reader compatible.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
