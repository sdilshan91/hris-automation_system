---
id: TC-CHR-ISO-029
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-029: Tenant A cannot see Tenant B's employee documents

## 1. Test Objective
Verify that the document management feature is fully tenant-isolated: a user authenticated in Tenant A sees only Tenant A's documents. Zero documents from Tenant B appear in any list, search, or API response. This tests EF Core global query filters and the `tenant_id` scoping on the `employee_documents` table per NFR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" has employee "Jane Doe" (emp-001-uuid) with 3 documents: contract.pdf, id-copy.jpg, cert.docx.
- Tenant "globex" has employee "Bob Smith" (emp-002-uuid) with 2 documents: offer-letter.pdf, visa.jpg.
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has 3 documents on Jane Doe |
| Tenant B | globex | Has 2 documents on Bob Smith |
| Auth Context | acme | HR Officer in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant. | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents`. | Response returns exactly 3 documents (contract.pdf, id-copy.jpg, cert.docx). All have `tenant_id` matching acme. |
| 3 | Send `GET /api/v1/tenant/employees/{emp-002-uuid}/documents` (using globex's employee ID). | Response returns 404 Not Found (employee does not exist in acme context due to EF global query filter). |
| 4 | Navigate to the Documents tab for Jane Doe in the UI. | Only Jane's 3 documents are visible. No globex documents. |
| 5 | Switch to "globex" tenant context and send `GET /api/v1/tenant/employees/{emp-002-uuid}/documents`. | Response returns exactly 2 documents (offer-letter.pdf, visa.jpg). Zero acme documents. |
| 6 | From globex context, attempt `GET /api/v1/tenant/employees/{emp-001-uuid}/documents`. | Response returns 404 Not Found. |

## 6. Postconditions
- No cross-tenant document data exposure occurred.
- EF Core global query filters correctly scoped all document queries by tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
