---
id: TC-CHR-ISO-015
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-015: RLS blocks direct DB queries across tenants for employee profiles

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) and EF Core global query filters prevent cross-tenant employee profile data access even when bypassing the API layer. This is a database-level isolation test per NFR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" (tenant_id = {acme_id}) has employees.
- Tenant "globex" (tenant_id = {globex_id}) has employees.
- Direct database access is available for this test (test environment only).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A ID | {acme_id} | UUID |
| Tenant B ID | {globex_id} | UUID |
| Acme Employee | Jane Doe | tenant_id = acme_id |
| Globex Employee | Eve Rogers | tenant_id = globex_id |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute SQL: `SELECT * FROM employees WHERE tenant_id = '{acme_id}'` | Returns only acme employees (Jane Doe, etc.). No globex employees. |
| 2 | Execute SQL: `SELECT * FROM employees WHERE tenant_id = '{globex_id}'` | Returns only globex employees (Eve Rogers, etc.). No acme employees. |
| 3 | Execute SQL: `SELECT * FROM employees WHERE id = '{eve_rogers_id}' AND tenant_id = '{acme_id}'` | Returns zero rows -- the WHERE clause enforces isolation. |
| 4 | Verify EF Core global query filter: simulate a DbContext query with acme tenant context set, then query for eve_rogers_id | Query returns null/empty -- the global filter appends `WHERE tenant_id = acme_id` even without explicit WHERE. |
| 5 | Verify that `IgnoreQueryFilters()` is NOT used in the employee profile read/edit code path | Code review check: the profile GET and PATCH handlers do not call `IgnoreQueryFilters()`. |

## 6. Postconditions
- Database-level tenant isolation confirmed.
- No cross-tenant data accessible via direct queries in the application's query paths.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
