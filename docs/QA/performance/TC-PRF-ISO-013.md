---
id: TC-PRF-ISO-013
user_story: US-PRF-004
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-013: Cycles created in Tenant A are invisible from Tenant B (cross-tenant read isolation) (NFR-2)

## 1. Test Objective
Verify NFR-2: all appraisal-cycle data (cycles, cycle_phases, cycle_participants) is isolated per tenant. An HR Officer authenticated in Tenant B cannot list or retrieve any cycle, phase, participant or dashboard belonging to Tenant A, including by direct ID. This exercises the platform's tenant-isolation mechanism (EF Core global query filters + TenantInterceptor).

> Note: US-PRF-004 NFR-2 / S7 specify PostgreSQL RLS policies (`tenant_id = current_setting('app.current_tenant_id')`) on the cycles/cycle_phases/cycle_participants tables. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added on these tables, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-004
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (cycles, cycle_phases, cycle_participants — tenant_id + RLS policy)

## 3. Preconditions
- Tenant "acme" has cycle "FY26 Annual Review" with known cycle/phase/participant IDs.
- Tenant "globex" has its own HR Officer and its own cycle(s).
- An HR Officer with cycle permissions is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has FY26 Annual Review |
| Tenant B | globex | has its own cycles |
| Auth context | globex | authenticated in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET /api/v1/tenant/performance/cycles` and any cycle list endpoint | Responses contain only globex cycles; zero acme cycles (NFR-2). |
| 3 | `GET .../cycles/{acme_cycle_id}`, `.../cycles/{acme_cycle_id}/dashboard`, `.../cycles/{acme_cycle_id}/phases` using acme IDs | 404 Not Found — global query filters exclude them; never 200 with acme data. |
| 4 | Verify at the DB level | `SELECT * FROM cycles WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme cycles/phases/participants. (If RLS exists, confirm a globex-set session cannot read acme rows even via direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own cycles; zero globex cycles. |

## 6. Postconditions
- No cross-tenant cycle/phase/participant/dashboard data is exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
