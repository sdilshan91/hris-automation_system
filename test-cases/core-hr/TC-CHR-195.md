---
id: TC-CHR-195
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-195: Upload 15 MB file is rejected with size limit error (negative)

## 1. Test Objective
Verify that uploading a file exceeding the 10 MB size limit is rejected with the exact error message "File exceeds the 10 MB limit." The file must not be stored, and no metadata record must be created. This validates AC-3 and FR-2.

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
| Employee | Jane Doe (emp-001-uuid) | Existing employee |
| File | large-scan.pdf | 15 MB (15,728,640 bytes), valid PDF |
| Category | Certificate | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab and click "Upload Document". | Upload form appears. |
| 2 | Select "large-scan.pdf" (15 MB) via file picker or drag-and-drop. | File is selected. File size "15 MB" is displayed. |
| 3 | Select category "Certificate" and click "Upload" / "Submit". | The system rejects the upload. |
| 4 | Verify the error message displayed. | Error message reads exactly: "File exceeds the 10 MB limit." |
| 5 | Verify the API response. | Response status is 400 Bad Request. Response body contains an error code or message indicating the file size exceeds the maximum. |
| 6 | Verify no document record was created in the database. | `employee_documents` table has no new row for "large-scan.pdf". |
| 7 | Verify no file was stored in object storage. | Object storage path for this employee has no "large-scan.pdf" file. |

## 6. Postconditions
- No document record exists for the rejected file.
- No file was persisted in object storage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
