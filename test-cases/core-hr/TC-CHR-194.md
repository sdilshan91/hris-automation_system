---
id: TC-CHR-194
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-194: Upload .exe file is rejected (negative)

## 1. Test Objective
Verify that uploading an executable file (.exe) is rejected by the system with a clear error message, regardless of file size or other metadata. Executable files are always blocked per BR-7. This validates AC-3 and BR-7.

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
| File | setup.exe | 2 MB, executable |
| Category | Other | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab and click "Upload Document". | Upload form appears. |
| 2 | Select "setup.exe" (2 MB) via file picker. | File is selected. |
| 3 | Select category "Other" and click "Upload" / "Submit". | The system rejects the upload. |
| 4 | Verify the error message displayed. | Error message reads: "File type not allowed. Supported: PDF, JPEG, PNG, DOCX." (or includes XLSX per BR-7 whitelist). |
| 5 | Verify the API response if the request reached the server. | Response status is 400 Bad Request with an error body indicating disallowed MIME type / file extension. |
| 6 | Verify no document record was created in the database. | `employee_documents` table has no new row for "setup.exe". |
| 7 | Verify no file was stored in object storage. | Object storage path for this employee has no "setup.exe" file. |
| 8 | Repeat with other blocked extensions: .bat, .sh, .js. | All are rejected with the same error message pattern. |

## 6. Postconditions
- No document record exists for the rejected file.
- No file was persisted in object storage.
- No virus scan was triggered (file rejected before scan stage).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
