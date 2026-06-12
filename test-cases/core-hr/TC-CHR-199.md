---
id: TC-CHR-199
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-199: Role-based access -- HR uploads/deletes, Employee views/downloads own only, Manager denied

## 1. Test Objective
Verify the role-based access control for document operations: HR Officers can upload and delete documents on any employee's record (BR-1); Employees can view and download documents on their own record only (BR-2, FR-10); Managers cannot access employee documents unless explicitly granted permission (BR-3). This validates FR-10 and BR-1/BR-2/BR-3.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-10
- Business Rules: BR-1, BR-2, BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee "Jane Doe" (emp-001-uuid) with Employee role has a document "contract.pdf" (doc-001-uuid).
- Employee "Bob Smith" (emp-002-uuid) with Employee role has a document "id-copy.pdf" (doc-002-uuid).
- Manager "Alice Mgr" has Employee role + Manager permissions, manages the team including Jane and Bob.
- HR Officer "HR Admin" is authenticated in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR Officer | HR Admin | Full doc CRUD on any employee |
| Employee A | Jane Doe (emp-001-uuid) | Owns contract.pdf |
| Employee B | Bob Smith (emp-002-uuid) | Owns id-copy.pdf |
| Manager | Alice Mgr | Manager role, no explicit doc permission |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **HR Officer: Upload** -- Authenticate as HR Admin. Upload "new-cert.pdf" to Jane Doe's record. | Upload succeeds. 201 Created. Document appears in Jane Doe's list. |
| 2 | **HR Officer: Delete** -- Delete "contract.pdf" from Jane Doe's record. | Confirmation modal appears. After confirming, deletion succeeds (soft delete). Document disappears from the visible list. `is_deleted = true` in DB. |
| 3 | **HR Officer: Upload on another employee** -- Upload "training-cert.pdf" to Bob Smith's record. | Upload succeeds. 201 Created. |
| 4 | **Employee: View own documents** -- Authenticate as Jane Doe. Navigate to own Documents tab. | Jane sees her documents (including "new-cert.pdf"). The "Upload Document" button is NOT visible (Employee cannot upload). The delete icon is NOT visible. |
| 5 | **Employee: Download own document** -- Jane clicks "Download" on "new-cert.pdf". | Download succeeds. Signed URL is generated. File downloads. |
| 6 | **Employee: Cannot view other employee's documents** -- Jane attempts `GET /api/v1/tenant/employees/{emp-002-uuid}/documents`. | Response returns 403 Forbidden (Jane can only access her own record). |
| 7 | **Employee: Cannot upload** -- Jane sends `POST /api/v1/tenant/employees/{emp-001-uuid}/documents` with a file. | Response returns 403 Forbidden. Only HR Officers can upload. |
| 8 | **Employee: Cannot delete** -- Jane sends `DELETE /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}`. | Response returns 403 Forbidden. Only HR Officers can delete. |
| 9 | **Manager: Cannot access documents** -- Authenticate as Alice Mgr. Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents`. | Response returns 403 Forbidden (BR-3: Managers cannot access employee documents unless explicitly granted). |
| 10 | **Manager: Cannot download** -- Alice sends `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-id}/download`. | Response returns 403 Forbidden. |

## 6. Postconditions
- HR Officer operations succeeded as expected.
- Employee could only view/download own documents.
- Manager was completely denied document access.
- All access attempts are logged in the audit trail.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
