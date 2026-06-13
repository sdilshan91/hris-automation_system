---
id: TC-LV-ISO-003
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-003: RLS blocks direct DB queries across tenants for leave types

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) policies on the `leave_type` table prevent direct database queries from returning data belonging to a different tenant, even when bypassing the application layer.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-2
- Data Requirements: Section 7 (RLS policy: tenant_isolation_select, tenant_isolation_modify)

## 3. Preconditions
- Tenant "acme" has leave types in the `leave_type` table.
- Tenant "globex" has leave types in the `leave_type` table.
- RLS policies `tenant_isolation_select` and `tenant_isolation_modify` are enabled on `leave_type`.
- A PostgreSQL session is established with the application's connection role (not superuser).

## 4. Test Data
| Tenant | Leave Types | tenant_id |
|--------|-------------|-----------|
| acme | Annual Leave, Sick Leave | acme_uuid |
| globex | Globex PTO | globex_uuid |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set PostgreSQL session variable to acme's tenant_id: `SET app.current_tenant_id = 'acme_uuid'` | Session configured for acme context. |
| 2 | Execute `SELECT * FROM leave_type` | Only acme's leave types returned (Annual Leave, Sick Leave). Globex PTO not visible. |
| 3 | Attempt `SELECT * FROM leave_type WHERE tenant_id = 'globex_uuid'` | Zero rows returned. RLS policy blocks cross-tenant access even with explicit WHERE clause. |
| 4 | Set session to globex: `SET app.current_tenant_id = 'globex_uuid'` | Session switched to globex. |
| 5 | Execute `SELECT * FROM leave_type` | Only globex's leave types returned. Acme's not visible. |
| 6 | Attempt `UPDATE leave_type SET name = 'Hacked' WHERE tenant_id = 'acme_uuid'` from globex session | Zero rows affected. `tenant_isolation_modify` policy prevents cross-tenant writes. |
| 7 | Attempt `DELETE FROM leave_type WHERE tenant_id = 'acme_uuid'` from globex session | Zero rows affected. RLS prevents cross-tenant deletes. |

## 6. Postconditions
- No cross-tenant data was read, modified, or deleted via direct DB queries.
- RLS policies are confirmed active and enforcing isolation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
