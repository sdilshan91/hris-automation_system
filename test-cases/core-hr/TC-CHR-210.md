---
id: TC-CHR-210
user_story: US-CHR-008
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-210: WCAG 2.1 AA accessibility for document management UI

## 1. Test Objective
Verify that the document management UI (document list, upload form, category filters, download/delete actions) meets WCAG 2.1 AA accessibility standards, including keyboard navigation, screen reader compatibility, color contrast, and focus management.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 3 documents with various categories and expiry states.
- Screen reader software is active (NVDA, VoiceOver, or JAWS).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Documents | 3 existing | contract.pdf (Contract), id-photo.jpg (ID), cert.docx (Certificate) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab using only the keyboard (Tab, Enter, Arrow keys). | The Documents section receives focus. Focus indicator is visible. |
| 2 | Tab through the category filter tabs (All, Contracts, IDs, Certificates, Other). | Each tab is focusable. Active tab is announced by screen reader (e.g., "All, tab, selected"). |
| 3 | Tab through the document list rows/cards. | Each document row/card is focusable. Screen reader announces: file name, category, upload date, size, expiry status. |
| 4 | Press Enter on the "Download" button of a document. | Download initiates. Screen reader announces the action (e.g., "Downloading contract.pdf"). |
| 5 | Press Enter on the "Delete" button of a document. | Confirmation modal opens. Focus moves to the modal. Screen reader announces the modal content. |
| 6 | Press Escape on the confirmation modal. | Modal closes. Focus returns to the delete button that triggered it. |
| 7 | Activate "Upload Document" via keyboard (Tab to button, press Enter). | Upload form/modal opens. Focus moves to the first form field. |
| 8 | Tab through the upload form fields: file picker, category dropdown, description, expiry date, submit button. | All form fields are reachable via Tab. Labels are associated with inputs (`for`/`id` or `aria-labelledby`). |
| 9 | Use the category dropdown via keyboard (Arrow keys to select, Enter to confirm). | Dropdown is keyboard-operable. Selected option is announced by screen reader. |
| 10 | Verify color contrast of expiry badges (green, amber, red). | All badge text meets WCAG AA minimum contrast ratio of 4.5:1 against the badge background, and the badge against the page background. |
| 11 | Verify that the drag-and-drop zone has an accessible alternative. | A "Browse files" button/link is available as an alternative to drag-and-drop. The drop zone has `aria-label` or descriptive text. |
| 12 | Run an automated accessibility audit (axe, Lighthouse). | No critical or serious WCAG 2.1 AA violations are reported for the Documents section. |

## 6. Postconditions
- All accessibility checks pass.
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
