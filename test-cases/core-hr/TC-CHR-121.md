---
id: TC-CHR-121
user_story: US-CHR-002
module: Core HR
priority: medium
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-121: WCAG 2.1 AA -- edit buttons have accessible labels, screen reader announces section headings

## 1. Test Objective
Verify that the employee profile page meets WCAG 2.1 AA accessibility standards: all edit buttons have descriptive accessible labels (not just icon-only), section headings are properly marked up for screen reader announcement, and keyboard navigation works across all sections. This validates NFR-6.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists with populated profile.
- Screen reader software (NVDA, VoiceOver, or equivalent) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access (edit icons visible) |
| Employee ID | {jane_doe_id} | Fully populated profile |
| Screen Reader | NVDA / VoiceOver | Accessibility testing tool |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's profile page using keyboard only (Tab to navigate) | Focus moves logically through the page elements: header, then section tabs, then first card. |
| 2 | Verify each section heading (Personal Info, Contact, Emergency Contacts, etc.) has proper heading markup (h2 or h3) | Headings are announced by screen reader with correct level (e.g., "Heading level 2: Personal Information"). |
| 3 | Tab to the edit button on the Personal Info card | Focus lands on the edit button. Screen reader announces "Edit Personal Information" (or equivalent descriptive label), NOT just "button" or "edit". |
| 4 | Tab to the edit button on the Contact card | Screen reader announces "Edit Contact Information". |
| 5 | Tab to the edit button on the Emergency Contacts card | Screen reader announces "Edit Emergency Contacts". |
| 6 | Activate an edit button using keyboard (Enter or Space) | Card transitions to edit mode. Focus moves to the first editable input field. |
| 7 | Navigate through all form fields in edit mode using Tab | Focus moves through each input field in logical order. Each field has an associated label announced by the screen reader. |
| 8 | Tab to the Save button and press Enter | Form submits. Focus returns to the card in read-only mode or a success notification is announced. |
| 9 | Tab to the Cancel button in edit mode | Screen reader announces "Cancel" or "Cancel editing". |
| 10 | Run automated accessibility audit (axe-core or Lighthouse) | No critical or serious WCAG 2.1 AA violations detected on the profile page. |
| 11 | Verify color contrast for status badges (Active=green, Probation=amber, Terminated=red, Suspended=gray) | All badge text-to-background contrast ratios meet WCAG AA minimum (4.5:1 for normal text, 3:1 for large text). |
| 12 | Verify the avatar image has appropriate alt text | Alt text reads "Profile photo of Jane Doe" or equivalent descriptive text. |

## 6. Postconditions
- All accessibility checks pass.
- No WCAG 2.1 AA violations identified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
