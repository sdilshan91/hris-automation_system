---
id: TC-CHR-211
user_story: US-CHR-008
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-211: Cross-browser compatibility for document management (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the document management UI (document list, upload with drag-and-drop and file picker, download, delete, category filters, expiry badges) renders and functions correctly on all supported browsers: Chrome (latest), Edge (latest), Firefox (latest), and Safari (latest).

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 3 documents.
- Browsers available: Chrome (latest), Edge (latest), Firefox (latest), Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable versions |
| Test File | test-upload.pdf | 1 MB, valid PDF |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Documents tab in **Chrome**. | Document list renders correctly. All columns/cards display. Expiry badges show correct colors. |
| 2 | Upload "test-upload.pdf" via drag-and-drop in Chrome. | Drag-and-drop works. Upload progress bar displays. Upload succeeds. |
| 3 | Download a document in Chrome. | Signed URL generated. File downloads correctly. |
| 4 | Delete a document in Chrome. | Confirmation modal renders. Delete succeeds. |
| 5 | Repeat steps 1-4 in **Edge**. | All behaviors are identical to Chrome. |
| 6 | Repeat steps 1-4 in **Firefox**. | All behaviors are identical. Drag-and-drop and file picker both work. |
| 7 | Repeat steps 1-4 in **Safari**. | All behaviors are identical. Note any Safari-specific file picker differences. Drag-and-drop works. |
| 8 | Verify the upload progress bar renders in all browsers. | Progress bar animation is smooth and displays percentage in all browsers. |
| 9 | Verify category filter tabs work in all browsers. | Tab switching filters the document list in all browsers. |

## 6. Postconditions
- Document management UI functions identically across all four browsers.
- No browser-specific rendering or functional issues identified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
