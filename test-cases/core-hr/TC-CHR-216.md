---
id: TC-CHR-216
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-216: Upload form displays all required fields (AC-1 detail)

## 1. Test Objective
Verify that clicking "Upload Document" on the employee's Documents tab opens a form with all required input elements per AC-1: file selection (drag-and-drop or file picker), document category dropdown (Contract, ID, Certificate, Other), optional description text field, and optional expiry date picker. The form layout matches the UI/UX specification.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User Role | HR Officer | Has upload permission |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab. | Documents section loads with "Upload Document" button visible. |
| 2 | Click "Upload Document". | Upload form/modal opens. |
| 3 | Verify file drop zone is present. | A dashed-border drag-and-drop area is displayed with text "Drop files here or click to browse" and a file icon. |
| 4 | Verify file picker is available. | Clicking the drop zone or a "Browse" button opens the native file picker dialog. |
| 5 | Verify Category dropdown is present. | A dropdown labeled "Category" or "Document Category" is displayed with options: Contract, ID, Certificate, Other. |
| 6 | Verify Description field is present. | A text input or textarea labeled "Description" is displayed. It is marked as optional. |
| 7 | Verify Expiry Date picker is present. | A date picker labeled "Expiry Date" is displayed. It is marked as optional. |
| 8 | Verify Submit/Upload button is present. | A button labeled "Upload" or "Submit" is displayed. It is disabled until a file is selected and category is chosen. |
| 9 | Verify Cancel button or close mechanism is present. | A "Cancel" button or "X" close icon allows dismissing the form without uploading. |
| 10 | Select a file and a category; leave description and expiry blank; click Upload. | Upload succeeds. Description is stored as null/empty. Expiry date is stored as null. |

## 6. Postconditions
- The upload form contains all fields specified in AC-1.
- Optional fields can be left blank without error.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
