---
id: TC-CHR-196
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-196: Upload disallowed MIME type (e.g., .svg, .html) is rejected (negative)

## 1. Test Objective
Verify that uploading a file with a MIME type not in the whitelist (PDF, JPEG, PNG, DOCX, XLSX) is rejected with the error message "File type not allowed. Supported: PDF, JPEG, PNG, DOCX." (or equivalent including XLSX). The file must not be stored. This validates AC-3 and FR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Business Rules: BR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Jane Doe" (emp-001-uuid) exists in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Can upload documents |
| File 1 | diagram.svg | 500 KB, image/svg+xml |
| File 2 | page.html | 20 KB, text/html |
| File 3 | data.csv | 100 KB, text/csv |
| Category | Other | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab and click "Upload Document". | Upload form appears. |
| 2 | Select "diagram.svg" (500 KB, image/svg+xml) via file picker. | File is selected. |
| 3 | Select category "Other" and click "Upload" / "Submit". | The system rejects the upload. |
| 4 | Verify the error message. | Error message reads: "File type not allowed. Supported: PDF, JPEG, PNG, DOCX." |
| 5 | Verify the API response. | Response status is 400 Bad Request with MIME type validation error. |
| 6 | Repeat steps 2-5 with "page.html" (text/html). | Same rejection and error message. |
| 7 | Repeat steps 2-5 with "data.csv" (text/csv). | Same rejection and error message. |
| 8 | Verify no document records were created for any of the rejected files. | `employee_documents` table has no new rows. |
| 9 | Verify no files were stored in object storage. | Object storage has no new files for this employee. |

## 6. Postconditions
- No document records exist for the rejected files.
- No files were persisted in object storage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
