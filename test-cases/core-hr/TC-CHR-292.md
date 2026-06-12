---
id: TC-CHR-292
user_story: US-CHR-011
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-292: Manager assignment UI meets WCAG 2.1 AA accessibility standards

## 1. Test Objective
Verify that the manager assignment UI (manager selector modal, My Team view, bulk action toolbar) meets WCAG 2.1 AA standards including keyboard navigation, screen reader compatibility, and color contrast. This validates NFR-4 accessibility requirements.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists (for manager assignment testing).
- Screen reader software is available (e.g., NVDA, VoiceOver).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Assistive tools | NVDA or VoiceOver | Screen reader |
| Lighthouse / axe | Browser extensions | Automated WCAG audit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile using only keyboard (Tab, Enter, Arrow keys). | All interactive elements are focusable. The Reporting Manager edit button can be reached and activated via keyboard. |
| 2 | Open the manager selector modal via keyboard (Enter/Space on edit button). | Modal opens. Focus is trapped within the modal. |
| 3 | Type in the search/autocomplete field. | Search results are announced by screen reader as they appear (using aria-live or equivalent). |
| 4 | Navigate search results using arrow keys. | Each result option is highlighted and announced by the screen reader (name, department, job title). |
| 5 | Select a result with Enter. | The selection is confirmed and announced. |
| 6 | Close the modal with Escape. | Modal closes. Focus returns to the edit button. |
| 7 | Navigate to the "My Team" view (if accessible via keyboard). | All direct-report cards are tabbable. Quick-action links are focusable. |
| 8 | Run an automated accessibility audit (axe or Lighthouse) on the manager selector modal and My Team page. | No WCAG 2.1 AA violations. Color contrast ratios for text and badges meet 4.5:1 (normal text) and 3:1 (large text) minimums. |
| 9 | Verify aria labels exist on the manager search field, results list, and action buttons. | All interactive elements have appropriate `aria-label`, `aria-describedby`, or `role` attributes. |

## 6. Postconditions
- No state change; accessibility verification only.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
