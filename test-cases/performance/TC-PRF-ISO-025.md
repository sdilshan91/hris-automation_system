---
id: TC-PRF-ISO-025
user_story: US-PRF-007
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-025: Dashboard in Tenant A shows ZERO Tenant B data -- cross-tenant aggregate read isolation (incl. by direct id)

## 1. Test Objective
Verify NFR-2: all dashboard aggregates (overview, distribution, department averages, top/bottom performers, trend, drill-down rosters, cycle progress) computed from the performance_summary materialized view are isolated per tenant. An HR Officer authenticated in Tenant B can never see, aggregate, or retrieve any Tenant A performance data -- including by passing Tenant A cycle/department ids directly. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor; performance_summary scoped by tenant_id).

> Note: US-PRF-007 NFR-2 / S7 specify PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on the performance_summary materialized view. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added on the view / its source tables, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-007
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (performance_summary materialized view, tenant_id scoped)

## 3. Preconditions
- Tenant "acme" (Tenant A) has cycle FY26 with submitted reviews and a populated performance_summary; known cycleId/departmentId/employeeId.
- Tenant "globex" (Tenant B) has its own cycles/reviews/summary and its own HR Officer.
- An HR Officer with `Performance.Read.All` is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has FY26 aggregates |
| Tenant B | globex | its own aggregates |
| Auth context | globex | Tenant B |
| acme ids | FY26 cycleId, eng deptId | used for direct-id probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/dashboard/overview` (+ top-performers, trend, departments) | All aggregates reflect ONLY globex data -- globex averages/completion/top-bottom; ZERO acme values mixed in (NFR-2). |
| 3 | `GET .../performance/dashboard/overview?cycleId={acme_FY26}` and `.../departments/{acme_engId}/employees` using acme ids | Empty / 404 -- the global query filter excludes acme rows; never 200 with acme aggregates or acme employee scores. |
| 4 | Verify at the DB level | `SELECT * FROM performance_summary WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme summary rows. (If RLS exists, confirm a globex-set session cannot read acme rows even via a direct query.) |
| 5 | Export as globex | The export contains only globex data; no acme rows appear in any CSV/XLSX/PDF. |
| 6 | Switch to acme and repeat | acme sees only its own aggregates; zero globex data. |

## 6. Postconditions
- No cross-tenant aggregate, distribution, performer, or roster data is exposed via dashboard API, drill-down, trend, or export.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
