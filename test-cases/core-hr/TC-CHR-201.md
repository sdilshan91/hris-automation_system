---
id: TC-CHR-201
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-201: Virus scan rejects EICAR test file on upload

## 1. Test Objective
Verify that the system scans uploaded files for malware using ClamAV (or equivalent) before persisting the storage reference. An EICAR test file (standard antivirus test signature) must be detected and rejected. No file must be stored and no metadata record created. This validates FR-4.

**Test Hint (from US-CHR-008 section 11):** Upload a test EICAR file; verify it is rejected.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Jane Doe" (emp-001-uuid) exists in tenant "acme".
- ClamAV or equivalent antivirus service is running and accessible.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Can upload documents |
| File | eicar-test.pdf | Contains the EICAR test string (X5O!P%@AP...) in a PDF wrapper; triggers AV detection |
| Category | Other | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab and click "Upload Document". | Upload form appears. |
| 2 | Select "eicar-test.pdf" containing the EICAR test string via file picker. | File is selected. MIME type and size pass initial validation. |
| 3 | Select category "Other" and click "Upload" / "Submit". | The system sends the file to the virus scanning service before persisting. |
| 4 | Verify the virus scan result. | The antivirus service detects the EICAR signature and flags the file as malicious. |
| 5 | Verify the upload is rejected. | An error message is displayed to the user (e.g., "File rejected: malware detected." or "The uploaded file failed security scanning."). Response status is 422 Unprocessable Entity or 400 Bad Request. |
| 6 | Verify no document record was created in the database. | `employee_documents` table has no new row for "eicar-test.pdf". |
| 7 | Verify no file was persisted in object storage. | The storage path for this employee has no "eicar-test.pdf" file. The file was either never written or was cleaned up after scan failure. |
| 8 | Verify an audit/security log entry was created for the rejected upload. | Log entry indicates: action = "malware_detected", file_name = "eicar-test.pdf", employee_id = emp-001-uuid. |

## 6. Postconditions
- No malicious file exists in object storage.
- No document metadata record was persisted.
- A security audit entry records the malware detection event.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
