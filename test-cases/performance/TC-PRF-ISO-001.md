---
id: TC-PRF-ISO-001
user_story: US-PRF-001
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-001: Goals created in Tenant A are invisible from Tenant B (cross-tenant read isolation) (NFR-2)

## 1. Test Objective
Verify NFR-2: all goal data is isolated per tenant. A manager/HR authenticated in Tenant B cannot list or retrieve any goal belonging to Tenant A, including by direct ID. This exercises the platform's tenant-isolation mechanism (EF Core global query filters + TenantInterceptor).

> Note: US-PRF-001 NFR-2 specifies PostgreSQL RLS policies (`tenant_id = current_setting('app.current_tenant_id')`) on the Goals table. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added on the Goals table, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-001
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (Goals table with tenant_id + RLS policy)

## 3. Preconditions
- Tenant "acme" has goals for employee Asha in cycle FY26-H1 (goal IDs known).
- Tenant "globex" has its own manager and goals.
- A manager/HR with goal permissions is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Asha's goals |
| Tenant B | globex | has its own goals |
| Auth context | globex | authenticated in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET /api/v1/performance/goals/team?cycleId=*` and any goal list endpoint | Responses contain only globex goals; zero acme goals (NFR-2). |
| 3 | `GET /api/v1/performance/goals/{acme_goal_id}` using an acme goal UUID | 404 Not Found — the global query filter excludes it; never 200 with acme's goal. |
| 4 | Verify at the DB level | `SELECT * FROM goals WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme rows. (If RLS exists, confirm a session set to globex cannot read acme rows even via direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own goals; zero globex goals. |

## 6. Postconditions
- No cross-tenant goal data is exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
