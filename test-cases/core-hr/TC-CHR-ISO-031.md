---
id: TC-CHR-ISO-031
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-031: RLS blocks direct DB queries across tenants for employee documents

## 1. Test Objective
Verify that even if a direct database query is executed (bypassing the application layer), the EF Core global query filter on the `employee_documents` table prevents reading documents belonging to a different tenant. This tests the defense-in-depth data isolation layer per NFR-2.

## 2. Related Requirements
- User Story: US-CHR-008
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" (tenant_id = `acme-uuid`) has employee documents in the `employee_documents` table.
- Tenant "globex" (tenant_id = `globex-uuid`) has employee documents in the same table.
- Direct database access is available for verification (test environment only).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme (acme-uuid) | Has documents |
| Tenant B | globex (globex-uuid) | Has documents |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using the application's DbContext with acme tenant context, execute `_dbContext.EmployeeDocuments.ToListAsync()`. | Returns ONLY documents where `tenant_id = acme-uuid`. Zero globex documents. |
| 2 | Using the application's DbContext with globex tenant context, execute `_dbContext.EmployeeDocuments.ToListAsync()`. | Returns ONLY documents where `tenant_id = globex-uuid`. Zero acme documents. |
| 3 | Verify the generated SQL includes the tenant filter. | SQL contains `WHERE ... tenant_id = @tenantId` (from the EF Core global query filter). |
| 4 | Using `IgnoreQueryFilters()` in a test context, execute `_dbContext.EmployeeDocuments.IgnoreQueryFilters().Where(d => d.TenantId == acmeUuid).ToListAsync()`. | Returns only acme documents. This confirms the tenant_id column is correctly populated. |
| 5 | Verify that a raw SQL query `SELECT * FROM employee_documents WHERE tenant_id = '{globex-uuid}'` from acme's application context still returns globex documents (raw SQL bypasses EF filters). | This confirms the isolation is at the EF Core level. If PostgreSQL RLS is also enabled, the raw query should also be filtered. |

## 6. Postconditions
- EF Core global query filters correctly isolate document data by tenant.
- No cross-tenant data leakage occurred through the application layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
