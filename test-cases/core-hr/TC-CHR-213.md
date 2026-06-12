---
id: TC-CHR-213
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-213: Document categorized list displays all metadata columns correctly

## 1. Test Objective
Verify that the document list on the employee profile's Documents tab displays all required metadata per FR-9: file icon (based on MIME type), file name, category tag, upload date, size (human-readable), uploader name, and expiry date/badge. Documents are organized in a categorized view.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-9
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 4 documents:
  - "contract.pdf" (PDF, Contract, 2 MB, uploaded by HR Admin, expires 2027-01-01)
  - "id-photo.jpg" (JPEG, ID, 500 KB, uploaded by HR Admin, no expiry)
  - "degree.png" (PNG, Certificate, 1.2 MB, uploaded by HR Admin, expires today + 5 days)
  - "notes.docx" (DOCX, Other, 300 KB, uploaded by HR Admin, no expiry)

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Doc A | contract.pdf | PDF, Contract, 2 MB, exp 2027-01-01 |
| Doc B | id-photo.jpg | JPEG, ID, 500 KB, no expiry |
| Doc C | degree.png | PNG, Certificate, 1.2 MB, exp today+5d |
| Doc D | notes.docx | DOCX, Other, 300 KB, no expiry |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab. | Document list loads with 4 documents. |
| 2 | Verify "contract.pdf" row. | Shows: PDF file icon, name "contract.pdf", category tag "Contract" (styled badge), upload date, size "2 MB", uploader "HR Admin", expiry badge (green, > 30 days). |
| 3 | Verify "id-photo.jpg" row. | Shows: JPEG/image file icon, name "id-photo.jpg", category tag "ID", upload date, size "500 KB", uploader "HR Admin", no expiry badge (column empty or dash). |
| 4 | Verify "degree.png" row. | Shows: PNG/image file icon, name "degree.png", category tag "Certificate", upload date, size "1.2 MB", uploader "HR Admin", expiry badge (red, < 7 days). |
| 5 | Verify "notes.docx" row. | Shows: DOCX/Word file icon, name "notes.docx", category tag "Other", upload date, size "300 KB", uploader "HR Admin", no expiry badge. |
| 6 | Verify file icons differ by MIME type. | PDF has a distinct icon from JPEG/PNG (image icon) and DOCX (document icon). |
| 7 | Verify human-readable file sizes. | Sizes are displayed as "2 MB", "500 KB", "1.2 MB", "300 KB" (not raw bytes). |

## 6. Postconditions
- All document metadata renders correctly.
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
