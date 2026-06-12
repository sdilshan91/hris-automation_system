---
id: TC-CHR-208
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-208: Responsive layout -- 360px viewport shows card stack and file picker instead of drag-drop

## 1. Test Objective
Verify that the document management UI is fully responsive on mobile viewports (360px): the document list becomes a card stack with file details stacked vertically, and drag-and-drop is disabled in favor of a file picker button. This validates NFR-5 and the UI/UX notes in section 8.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 3 documents uploaded.
- Browser or device viewport is set to 360px width.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport Width | 360px | Mobile breakpoint |
| Documents | 3 existing documents | contract.pdf, id-copy.jpg, cert.docx |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set browser viewport to 360px width (using DevTools or a mobile device). | Viewport is confirmed at 360px. |
| 2 | Navigate to Jane Doe's Documents tab. | The Documents section loads. |
| 3 | Verify the document list renders as a card stack. | Each document is rendered as a vertical card with file icon, file name, category tag, upload date, size, and expiry badge stacked vertically. No horizontal table layout. |
| 4 | Verify each card has download and delete action buttons. | Download (arrow-down) and delete (trash) icons are accessible and tappable with adequate touch target size (minimum 44x44px). |
| 5 | Click "Upload Document". | The upload form opens. |
| 6 | Verify that the drag-and-drop zone is NOT displayed. | Instead of a dashed-border drop zone, a "Choose File" or "Browse" button is displayed for file selection. |
| 7 | Tap the file picker button. | The native file picker dialog opens on the mobile device/browser. |
| 8 | Select a file and complete the upload form. | Upload succeeds. The new document card appears in the stack. |
| 9 | Resize viewport to 768px. | The document list transitions to the desktop layout (table/row format with drag-and-drop enabled). |
| 10 | Resize viewport to 1920px. | Full desktop layout with drag-and-drop zone, table columns, and all action buttons. |

## 6. Postconditions
- The document management UI adapts correctly across all tested viewport widths.
- All functionality (view, upload, download, delete) works on mobile.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
