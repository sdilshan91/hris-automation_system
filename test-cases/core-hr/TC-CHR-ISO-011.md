---
id: TC-CHR-ISO-011
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-011: RLS blocks direct DB queries across tenants for employees

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) policies on the employees table prevent direct database queries from returning employee records belonging to a different tenant, even when bypassing the application layer (defense-in-depth).

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" (Tenant A) and "globex" (Tenant B), both with employees.
- PostgreSQL RLS policies are enabled on the employees table.
- A database connection with Tenant A's role/context is available for direct SQL queries.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A ID | tenant-a-uuid | "acme" tenant |
| Tenant B ID | tenant-b-uuid | "globex" tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Connect to the database with Tenant A's RLS context (e.g., `SET app.current_tenant_id = 'tenant-a-uuid'`) | Connection established with Tenant A context. |
| 2 | Execute: `SELECT * FROM employees` | Only Tenant A's employees are returned. No Tenant B employees appear. |
| 3 | Execute: `SELECT * FROM employees WHERE tenant_id = 'tenant-b-uuid'` | Zero rows returned. RLS prevents reading Tenant B's data even with an explicit WHERE clause. |
| 4 | Attempt: `UPDATE employees SET first_name = 'Hacked' WHERE tenant_id = 'tenant-b-uuid'` | Zero rows affected. RLS prevents modifying Tenant B's data. |
| 5 | Attempt: `DELETE FROM employees WHERE tenant_id = 'tenant-b-uuid'` | Zero rows affected. RLS prevents deleting Tenant B's data. |
| 6 | Switch context to Tenant B and verify Tenant A's data is similarly protected | Same isolation in reverse direction. |

## 6. Postconditions
- RLS provides defense-in-depth tenant isolation at the database level.
- No cross-tenant data access is possible via direct SQL.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
