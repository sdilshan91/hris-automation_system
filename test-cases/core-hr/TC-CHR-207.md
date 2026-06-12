---
id: TC-CHR-207
user_story: US-CHR-008
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-207: Audit trail -- document view and download events logged

## 1. Test Objective
Verify that every document view (listing) and download action is logged in the audit trail for compliance purposes. The audit log must capture the user ID, action type, document ID, employee ID, timestamp, and tenant context. This validates NFR-6.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-6
- Functional Requirements: FR-6, FR-9

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer "HR Admin" is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has documents "contract.pdf" (doc-001-uuid) and "id-copy.pdf" (doc-002-uuid).
- The audit log table is accessible for verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR Officer | HR Admin | Performs view and download |
| Employee | Jane Doe (emp-001-uuid) | Document owner |
| Doc A | contract.pdf (doc-001-uuid) | Will be downloaded |
| Doc B | id-copy.pdf (doc-002-uuid) | Will be viewed in list |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab (triggering `GET /api/v1/tenant/employees/{emp-001-uuid}/documents`). | Document list loads with both documents. |
| 2 | Verify an audit log entry was created for the document list view. | Audit log contains: action = "view_document_list" or "access", entity = "employee_documents", employee_id = emp-001-uuid, user_id = HR Admin ID, tenant_id = acme, timestamp = now. |
| 3 | Click "Download" on "contract.pdf". | Download succeeds via signed URL. |
| 4 | Verify an audit log entry was created for the download. | Audit log contains: action = "download", entity = "employee_document", document_id = doc-001-uuid, employee_id = emp-001-uuid, user_id = HR Admin ID, tenant_id = acme, timestamp = now. |
| 5 | Authenticate as Jane Doe (Employee role). Navigate to own Documents tab and download "id-copy.pdf". | Download succeeds. |
| 6 | Verify audit log entries for Jane's access. | Two entries: one for document list view, one for download of id-copy.pdf. Both have user_id = Jane Doe's user ID, tenant_id = acme. |

## 6. Postconditions
- Audit log contains entries for all document access events.
- Each entry includes sufficient detail for compliance review: who, what, when, from which tenant.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
