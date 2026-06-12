---
id: TC-CHR-ISO-007
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-007: RLS blocks direct DB queries across tenants for job titles

## 1. Test Objective
Verify that the EF Core global query filter on the `job_title` entity (and any database-level row-level security if applied) prevents direct database queries from returning job titles belonging to a different tenant. This tests the defense-in-depth layer beyond the API.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" (Tenant A) with `tenant_id = {uuid_a}` has job titles in the database.
- Tenant "globex" (Tenant B) with `tenant_id = {uuid_b}` has job titles in the database.
- Direct database access is available for verification.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A ID | {uuid_a} | acme tenant |
| Tenant B ID | {uuid_b} | globex tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using the application's `AppDbContext` with Tenant A's context set, execute `dbContext.JobTitles.ToListAsync()` | Only job titles with `tenant_id = {uuid_a}` are returned. No records with `tenant_id = {uuid_b}` appear. |
| 2 | Using the application's `AppDbContext` with Tenant B's context set, execute `dbContext.JobTitles.ToListAsync()` | Only job titles with `tenant_id = {uuid_b}` are returned. |
| 3 | Verify the global query filter expression in `AppDbContext.OnModelCreating` for the job title entity | A filter of the form `jt => !jt.IsDeleted && (!_tenantContext.IsResolved \|\| jt.TenantId == _tenantContext.TenantId)` is applied. |
| 4 | Execute a raw SQL query `SELECT * FROM job_titles WHERE tenant_id = '{uuid_b}'` while the application context is set to Tenant A | If EF global filters are bypassed (raw SQL), verify that application-level code never uses raw queries without tenant filtering, or that database-level RLS policies block the query. |
| 5 | Verify that `IgnoreQueryFilters()` is not used in any job title repository or service code | Code review confirms no unintentional filter bypass. |

## 6. Postconditions
- The global query filter is confirmed to be active and correctly scoped for job titles.
- No cross-tenant data leakage is possible through the ORM layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
