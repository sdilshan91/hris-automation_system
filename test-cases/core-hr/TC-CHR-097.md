---
id: TC-CHR-097
user_story: US-CHR-001
module: Core HR
priority: medium
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-097: WCAG 2.1 AA accessibility -- keyboard navigation and screen reader (NFR-5)

## 1. Test Objective
Verify that the employee creation form meets WCAG 2.1 AA accessibility standards: fully keyboard-navigable, screen-reader-friendly labels for all fields, sufficient color contrast, focus indicators visible, and validation errors announced by assistive technology.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- Screen reader software (NVDA, JAWS, or VoiceOver) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Screen reader | NVDA / VoiceOver | Assistive technology |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Add Employee form using keyboard only (Tab, Enter) | The "Add Employee" button is reachable via Tab and activatable via Enter. |
| 2 | Tab through all form fields in the Personal Info section | Focus moves sequentially through all fields. Each field has a visible focus indicator (ring/outline). |
| 3 | Verify screen reader announces each field's label when focused | Screen reader reads "First Name", "Last Name", "Email", etc. for each input. |
| 4 | Navigate between wizard steps using keyboard | Steps can be navigated via keyboard (Tab to Next/Previous buttons, or arrow keys if step indicators are interactive). |
| 5 | Trigger a validation error (e.g., submit with empty first_name) | Screen reader announces the error message (e.g., "Error: First name is required."). Error is associated with the field via aria-describedby or aria-invalid. |
| 6 | Verify color contrast ratio of text on all form elements | All text meets WCAG 2.1 AA contrast ratio of at least 4.5:1 (normal text) or 3:1 (large text). |
| 7 | Verify the profile photo upload zone is keyboard-accessible | The drag-and-drop zone can be activated via keyboard (Enter/Space to open file picker). |
| 8 | Verify progress indicator (step dots/breadcrumbs) has aria-label or equivalent | Screen reader announces the current step (e.g., "Step 1 of 7: Personal Info"). |
| 9 | Verify dropdown fields (department, job title, employment type) are keyboard-operable | Dropdowns open with Enter/Space, navigate with arrow keys, select with Enter. |

## 6. Postconditions
- All wizard sections are fully accessible via keyboard and screen reader.
- WCAG 2.1 AA compliance verified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
