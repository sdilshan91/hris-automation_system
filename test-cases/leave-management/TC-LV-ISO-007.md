---
id: TC-LV-ISO-007
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-007: RLS blocks direct DB queries across tenants for entitlement data

## 1. Test Objective
Verify that EF Core global query filters (and PostgreSQL RLS if implemented) prevent direct database queries from returning entitlement rules, overrides, or leave ledger entries belonging to a different tenant. This tests the data layer isolation independent of the API layer.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with entitlement rules, overrides, and leave ledger entries.
- Tenant "globex" exists with its own entitlement rules, overrides, and leave ledger entries.
- Direct database access is available for verification.

## 4. Test Data
| Table | Tenant A (acme) | Tenant B (globex) |
|-------|----------------|-------------------|
| leave_entitlement_rule | 3 rules | 2 rules |
| leave_entitlement_override | 1 override | 1 override |
| leave_ledger | 10+ entries | 8+ entries |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute direct SQL: `SELECT * FROM leave_entitlement_rule WHERE tenant_id = '{acme_id}'` | Returns only acme's 3 rules. Zero globex rules. |
| 2 | Execute direct SQL: `SELECT * FROM leave_entitlement_rule WHERE tenant_id = '{globex_id}'` | Returns only globex's 2 rules. Zero acme rules. |
| 3 | Execute direct SQL: `SELECT * FROM leave_entitlement_override WHERE tenant_id = '{acme_id}'` | Returns only acme's override. |
| 4 | Execute direct SQL: `SELECT * FROM leave_ledger WHERE tenant_id = '{acme_id}'` | Returns only acme's ledger entries. |
| 5 | Via the EF Core DbContext with acme tenant context, query `dbContext.LeaveEntitlementRules.ToListAsync()` | Global query filter ensures only acme's rules are returned. |
| 6 | Via the EF Core DbContext with acme tenant context, attempt `dbContext.LeaveEntitlementRules.IgnoreQueryFilters().Where(r => r.TenantId == globex_id).ToListAsync()` | This bypasses filters -- verify no application code path uses `IgnoreQueryFilters()` for entitlement queries (code review check). |
| 7 | Verify `leave_entitlement_rule` table has `tenant_id` column with NOT NULL constraint | Column is NOT NULL; no orphaned rows without tenant assignment. |
| 8 | Verify `leave_entitlement_override` table has `tenant_id` column with NOT NULL constraint | Column is NOT NULL. |
| 9 | Verify `leave_ledger` table has `tenant_id` column with NOT NULL constraint | Column is NOT NULL. |

## 6. Postconditions
- All entitlement-related tables enforce tenant isolation at the data layer.
- No cross-tenant data accessible via EF Core with active query filters.
- `tenant_id` is NOT NULL on all relevant tables.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
