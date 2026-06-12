---
id: TC-CHR-ISO-039
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-039: RLS blocks direct DB queries across tenants for bulk-imported employee data

## 1. Test Objective
Verify that the EF Core global query filter (and PostgreSQL RLS if configured) prevents cross-tenant access to bulk-imported employee records even when using direct database queries that bypass the application API layer. This validates NFR-2 at the data layer.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant A ("acme") and Tenant B ("globex") exist.
- 5 employees were imported via bulk import into Tenant A.
- Direct database access (psql or equivalent) is available for testing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A UUID | acme-uuid | Imported employees belong here |
| Tenant B UUID | globex-uuid | Should not see Tenant A data |
| Imported Employee IDs | emp1-uuid through emp5-uuid | Created via bulk import |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using EF Core DbContext scoped to Tenant B, execute `dbContext.Employees.ToListAsync()`. | Result set contains zero of the 5 employees imported into Tenant A. The global query filter `WHERE tenant_id = @tenantBId` excludes them. |
| 2 | Using EF Core DbContext scoped to Tenant B, execute `dbContext.Employees.Where(e => e.Id == emp1-uuid).FirstOrDefaultAsync()`. | Returns null. The employee exists in the DB but is filtered out by the tenant scope. |
| 3 | Using EF Core DbContext scoped to Tenant A, execute `dbContext.Employees.ToListAsync()`. | Returns all 5 imported employees (plus any other Tenant A employees). |
| 4 | (If PostgreSQL RLS is enabled) Connect via psql with Tenant B's role/context. Run `SELECT * FROM employees WHERE id = 'emp1-uuid'`. | Zero rows returned. RLS blocks the query. |

## 6. Postconditions
- Cross-tenant data access is blocked at the database query level.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
