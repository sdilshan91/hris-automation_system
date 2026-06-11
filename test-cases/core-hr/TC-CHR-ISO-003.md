---
id: TC-CHR-ISO-003
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-ISO-003: RLS blocks direct DB queries across tenants for departments

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) policies, in addition to EF Core global query filters, prevent cross-tenant department data access even when EF filters are bypassed (e.g., via `IgnoreQueryFilters()` or raw SQL).

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-3

## 3. Preconditions
- Tenant "acme" exists with departments.
- Tenant "globex" exists with departments.
- RLS policies are configured on the `department` table in PostgreSQL.
- Database connection context is set to acme's tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Active, has departments |
| Tenant B | globex | Active, has departments |
| DB Session Context | acme tenant_id | Set via PostgreSQL session variable |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Connect to the database with acme's tenant context set in the session (e.g., `SET app.current_tenant_id = 'acme_uuid'`) | Connection established with tenant context. |
| 2 | Execute `SELECT * FROM department` | Only acme's departments are returned. Globex departments are invisible due to RLS. |
| 3 | Execute `SELECT * FROM department WHERE tenant_id = 'globex_uuid'` | Returns zero rows. RLS prevents access even with explicit tenant_id filter. |
| 4 | Attempt `INSERT INTO department (tenant_id, name, ...) VALUES ('globex_uuid', 'Rogue Dept', ...)` | Insert is blocked by RLS policy (cannot write to another tenant's data). |
| 5 | Attempt `UPDATE department SET name = 'Hacked' WHERE tenant_id = 'globex_uuid'` | Zero rows affected. RLS prevents cross-tenant updates. |
| 6 | Attempt `DELETE FROM department WHERE tenant_id = 'globex_uuid'` | Zero rows affected. RLS prevents cross-tenant deletes. |

## 6. Postconditions
- No cross-tenant data was read, created, modified, or deleted.
- RLS enforcement is verified at the database level.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
