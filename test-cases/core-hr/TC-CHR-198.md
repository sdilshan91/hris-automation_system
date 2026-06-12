---
id: TC-CHR-198
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-198: Tenant isolation on document list -- Tenant A documents not visible to Tenant B

## 1. Test Objective
Verify that the document list for an employee is fully tenant-isolated: a user in Tenant B cannot see any documents belonging to Tenant A employees, even if they know the employee ID. The EF Core global query filter and storage path prefixing ensure complete isolation. This validates NFR-2 and FR-3.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-3, FR-9
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" has employee "Jane Doe" (emp-001-uuid) with 3 documents uploaded.
- Tenant "globex" has employee "Bob Smith" (emp-002-uuid) with 1 document uploaded.
- HR Officer is authenticated in the "globex" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has 3 documents on Jane Doe |
| Tenant B | globex | Has 1 document on Bob Smith |
| Auth Context | globex | HR Officer in Tenant B |
| Acme Employee ID | emp-001-uuid | Not in globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "globex" tenant. | JWT contains tenant_id for globex. |
| 2 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents` (using acme's employee ID). | Response returns 404 Not Found (employee does not exist in globex context due to EF global query filter). Not 403. |
| 3 | Send `GET /api/v1/tenant/employees/{emp-002-uuid}/documents` (using globex's own employee). | Response returns 200 with exactly 1 document belonging to Bob Smith. No acme documents are present. |
| 4 | Verify the document list in the response contains only globex-scoped data. | Every `tenant_id` in the response matches globex. Zero acme tenant IDs appear. |
| 5 | Switch to acme context. Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents`. | Response returns 200 with exactly 3 documents belonging to Jane Doe. No globex documents. |
| 6 | From acme context, attempt `GET /api/v1/tenant/employees/{emp-002-uuid}/documents`. | Response returns 404 Not Found (Bob Smith does not exist in acme context). |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scoped all document and employee queries by tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
