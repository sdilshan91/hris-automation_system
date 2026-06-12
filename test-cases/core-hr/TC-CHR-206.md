---
id: TC-CHR-206
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-206: Soft delete -- is_deleted set to true, file retained in storage

## 1. Test Objective
Verify that when an HR Officer deletes a document, it is soft-deleted: the `is_deleted` column is set to `true` in the database, the document disappears from the UI document list, but the file remains in object storage for the configured retention period. An audit trail records the deletion. This validates FR-7 and BR-5.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-7
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has a document "old-contract.pdf" (doc-099-uuid) in her record.
- The file "old-contract.pdf" exists in object storage at `{acme-tenant-id}/core-hr/emp-001-uuid/2026/01/old-contract.pdf`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Document | old-contract.pdf (doc-099-uuid) | Existing, active document |
| Storage Path | {acme-tenant-id}/core-hr/emp-001-uuid/2026/01/old-contract.pdf | Known path |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab. | "old-contract.pdf" is visible in the document list with a delete icon (trash). |
| 2 | Click the delete icon (trash) on "old-contract.pdf". | A confirmation modal appears: "Are you sure you want to delete this document? This action cannot be undone." (or similar). |
| 3 | Click "Confirm" / "Delete" on the confirmation modal. | The document disappears from the UI list. A success toast appears (e.g., "Document deleted successfully"). |
| 4 | Verify the API request `DELETE /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-099-uuid}`. | Response status is 200 OK or 204 No Content. |
| 5 | Query the `employee_documents` table for doc-099-uuid. | Record still exists with `is_deleted = true`. `updated_at` is updated to now. |
| 6 | Verify the document no longer appears in the default document list API response. | `GET /api/v1/tenant/employees/{emp-001-uuid}/documents` does not include "old-contract.pdf" (filtered by `is_deleted = false`). |
| 7 | Verify the file still exists in object storage. | The file at `{acme-tenant-id}/core-hr/emp-001-uuid/2026/01/old-contract.pdf` still exists. It has NOT been deleted from storage. |
| 8 | Verify an audit log entry was created for the deletion. | Audit log contains: action = "delete" (soft), entity = "employee_document", document_id = doc-099-uuid, user_id = HR Officer's ID. |

## 6. Postconditions
- The document record has `is_deleted = true` but still exists in the database.
- The physical file remains in object storage (retained for the configured retention period).
- The document is invisible in the default UI and API responses.
- An audit trail entry records the deletion.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
