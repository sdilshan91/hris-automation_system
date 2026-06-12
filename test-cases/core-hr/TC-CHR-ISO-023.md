---
id: TC-CHR-ISO-023
user_story: US-CHR-006
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-023: RLS blocks direct DB queries across tenants for org-tree data

## 1. Test Objective
Verify that Row-Level Security (PostgreSQL RLS) and EF Core global query filters prevent direct database queries from returning departments or employees belonging to a different tenant, even if the query is crafted to bypass application logic. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" (tenant_id: AAA-111) exists with departments and employees.
- Tenant "globex" (tenant_id: BBB-222) exists with departments and employees.
- Direct database access is available for the test (e.g., psql session or test harness with raw SQL execution).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A ID | AAA-111 | acme |
| Tenant B ID | BBB-222 | globex |
| Acme Department | Engineering | In acme |
| Globex Department | Marketing | In globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using the application DbContext scoped to Tenant A (acme), execute `dbContext.Departments.ToListAsync()` | Result contains only acme departments ("Engineering" etc.). No globex departments ("Marketing"). |
| 2 | Using the application DbContext scoped to Tenant A, execute `dbContext.Departments.Where(d => d.TenantId == "BBB-222").ToListAsync()` | Result is empty. The global query filter overrides the explicit tenant_id filter, preventing cross-tenant access. |
| 3 | Using raw SQL via the DbContext scoped to Tenant A, execute `SELECT * FROM departments WHERE tenant_id = 'BBB-222'` | If RLS is enabled, the result is empty. If RLS is not yet enabled (application-layer only), this query MAY return globex data -- document the finding. |
| 4 | Repeat steps 1-3 for the employees table | Same isolation results: only acme employees returned. Cross-tenant employee queries return empty. |
| 5 | Using the application DbContext, call `dbContext.Departments.IgnoreQueryFilters().Where(d => d.TenantId == "BBB-222").ToListAsync()` from Tenant A context | If RLS is enforced at the DB level, result is still empty. If only EF Core filters are used, this bypasses them -- document whether this is a risk. |

## 6. Postconditions
- No data was modified.
- Isolation boundaries are documented.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
