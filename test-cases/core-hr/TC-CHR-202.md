---
id: TC-CHR-202
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-202: Boundary -- exactly 10 MB file allowed, 10 MB + 1 byte rejected

## 1. Test Objective
Verify the exact boundary of the 10 MB file size limit: a file of exactly 10,485,760 bytes (10 MB) is accepted, while a file of 10,485,761 bytes (10 MB + 1 byte) is rejected with the "File exceeds the 10 MB limit." error message. This validates the boundary condition of AC-3 and FR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Jane Doe" (emp-001-uuid) exists in tenant "acme".
- Two test PDF files prepared: one at exactly 10,485,760 bytes, one at 10,485,761 bytes.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| File A | boundary-exact.pdf | Exactly 10,485,760 bytes (10 MB) |
| File B | boundary-over.pdf | Exactly 10,485,761 bytes (10 MB + 1 byte) |
| Category | Certificate | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload "boundary-exact.pdf" (10,485,760 bytes) to Jane Doe's record with category "Certificate". | Upload succeeds. Response status is 201 Created. Document appears in the list with size displayed as "10 MB". |
| 2 | Verify the document record in the database. | `file_size_bytes` = 10485760. All metadata is correct. |
| 3 | Verify the file exists in object storage. | File is present at the expected tenant-isolated path. |
| 4 | Upload "boundary-over.pdf" (10,485,761 bytes) to Jane Doe's record with category "Certificate". | Upload is rejected. |
| 5 | Verify the error message. | Error message reads: "File exceeds the 10 MB limit." |
| 6 | Verify the API response for the rejected file. | Response status is 400 Bad Request. |
| 7 | Verify no document record was created for the rejected file. | No row exists for "boundary-over.pdf" in `employee_documents`. |
| 8 | Verify no file was stored for the rejected upload. | Object storage has no "boundary-over.pdf". |

## 6. Postconditions
- "boundary-exact.pdf" exists as a valid document record and in object storage.
- "boundary-over.pdf" was fully rejected with no artifacts.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
