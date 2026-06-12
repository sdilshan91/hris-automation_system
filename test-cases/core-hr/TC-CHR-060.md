---
id: TC-CHR-060
user_story: US-CHR-005
module: Core HR
priority: medium
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-060: Job titles management UI accessibility (WCAG 2.1 AA)

## 1. Test Objective
Verify that the Job Titles management page meets WCAG 2.1 Level AA accessibility standards, including keyboard navigation, screen reader compatibility, color contrast, and form accessibility.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Job titles exist in the tenant (mix of active and inactive).
- Screen reader software is available (e.g., NVDA, VoiceOver).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Tools | axe DevTools, NVDA/VoiceOver | Accessibility testing tools |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles page using keyboard only (Tab, Enter, Escape) | All interactive elements (buttons, links, form fields, table rows) are reachable via Tab key. Focus order is logical (top-to-bottom, left-to-right). |
| 2 | Verify visible focus indicators on all interactive elements | Each focused element has a visible outline or highlight that meets 3:1 contrast ratio against the background. |
| 3 | Open the "Add Job Title" modal/panel using keyboard (Tab to button, press Enter) | Modal opens. Focus moves to the first form field inside the modal. |
| 4 | Fill out the form using keyboard only (Tab between fields, type values, select from Grade dropdown) | All form fields are operable via keyboard. The Grade searchable dropdown can be navigated with arrow keys. |
| 5 | Close the modal using Escape key | Modal closes. Focus returns to the "Add Job Title" button. |
| 6 | Run axe DevTools accessibility audit on the Job Titles page | No critical or serious violations. All elements have proper ARIA labels, roles, and states. |
| 7 | Activate a screen reader and navigate the page | Table data is announced correctly with column headers. Status (Active/Inactive) is announced. Action buttons have accessible labels (not just icons). |
| 8 | Verify color contrast for all text elements | All text meets 4.5:1 contrast ratio for normal text and 3:1 for large text per WCAG 2.1 AA. |
| 9 | Verify that status indicators are not conveyed by color alone | Active/Inactive status has text labels or icons in addition to color differences. |
| 10 | Verify form validation errors are announced to screen readers | Error messages are associated with their form fields via `aria-describedby` or equivalent. |

## 6. Postconditions
- No accessibility violations detected at WCAG 2.1 AA level.
- All interactive elements are keyboard-operable and screen-reader-compatible.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
