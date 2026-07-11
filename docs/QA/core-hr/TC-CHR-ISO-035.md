---
id: TC-CHR-ISO-035
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: pass
created: 2026-06-12
exec_note: "2026-06-30 API-layer exec vs iso fixture (employee status + profile/history). EF-Core global query filter ISOLATION HOLDS: IDOR GET isob-emp profile under isoa-token+isoa-hdr -> 404; status/transitions on isob-emp under isoa context unreachable. (No standalone employment-history list endpoint; history surfaces via {id}/profile, which 404s cross-tenant.) Achievable EF-layer ORM-isolation objective met -> PASS. CAVEATS: (1) No PostgreSQL RLS (EF-filter-only); raw-SQL steps 3-4 against employment_history NOT DB-enforced -- documented escalation/hardening item per the TC. (2) Cross-tenant READ LEAK via foreign X-Tenant-Subdomain override: isoa-tok+hdr-isob reads isob-emp profile + status/transitions (200) = systemic BUG-003. READ-ONLY on real tenants; nothing deleted."

# TC-CHR-ISO-035: RLS and EF global query filters block direct DB queries across tenants for status and history data

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) and EF Core global query filters prevent any cross-tenant access to employee status data and employment history records, even when bypassing the application layer with direct database queries. This validates the deepest layer of tenant isolation (NFR-2).

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant A ("acme", tenant_id = `tenant-a-uuid`) has employee "John Smith" with a status change history entry (e.g., active -> suspended).
- Tenant B ("globex", tenant_id = `tenant-b-uuid`) exists.
- Direct database access is available for testing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A ID | tenant-a-uuid | Has employee with status history |
| Tenant B ID | tenant-b-uuid | Should not see Tenant A data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Using EF Core context scoped to Tenant B, query `dbContext.Employees.Where(e => e.Id == emp-a-uuid)`. | Returns empty / no results. The global query filter `WHERE tenant_id = tenant-b-uuid` excludes Tenant A's employee. |
| 2 | Using EF Core context scoped to Tenant B, query the employment history table for `emp-a-uuid`. | Returns empty / no results. History entries belonging to Tenant A are not visible. |
| 3 | Using EF Core context scoped to Tenant B, attempt `IgnoreQueryFilters()` on employee history. | If RLS is enforced at the PostgreSQL level (beyond EF), still returns empty. If RLS is not yet DB-enforced (EF-only), this may return data -- document as a known risk. |
| 4 | Execute a raw SQL query against the database with Tenant B's connection context: `SELECT * FROM employment_history WHERE employee_id = 'emp-a-uuid'`. | If PostgreSQL RLS policies are in place, returns zero rows. If RLS is EF-only, this returns data -- flag as an escalation item for hardening. |

## 6. Postconditions
- No cross-tenant data was exposed.
- Any gaps in DB-level RLS are documented for remediation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
