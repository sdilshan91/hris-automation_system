---
id: TC-PRF-ISO-017
user_story: US-PRF-005
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-017: 360 feedback in Tenant A is invisible from Tenant B (cross-tenant read isolation) (NFR-2)

## 1. Test Objective
Verify NFR-2: all 360 data (feedback_360 records, assignments, aggregated results, summary reports) is isolated per tenant. A user authenticated in Tenant B cannot list or retrieve any 360 assignment, feedback record, results view, or report belonging to Tenant A, including by direct ID. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor).

> Note: US-PRF-005 NFR-2 / S7 specify PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on the feedback_360 table. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added on feedback_360, extend Step 4 to assert isolation at the DB session level as defense-in-depth.

## 2. Related Requirements
- User Story: US-PRF-005
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (feedback_360 with tenant_id + RLS policy)

## 3. Preconditions
- Tenant "acme" has a 360 review for Liam Carter with known assignment / feedback / results IDs.
- Tenant "globex" has its own HR Officer and its own 360 reviews.
- An HR Officer with `Performance.Review.All` is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Liam's 360 review |
| Tenant B | globex | its own 360 data |
| Auth context | globex | Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/360/...` list/results endpoints | Responses contain only globex 360 data; zero acme records (NFR-2). |
| 3 | `GET .../performance/360/{acme_revieweeId}/results`, `.../feedback/{acme_assignmentId}`, `.../{acme_revieweeId}/report` using acme IDs | 404 Not Found — global query filters exclude them; never 200 with acme data. |
| 4 | Verify at the DB level | `SELECT * FROM feedback_360 WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme feedback. (If RLS exists, confirm a globex-set session cannot read acme rows even via direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own 360 data; zero globex records. |

## 6. Postconditions
- No cross-tenant 360 feedback/assignment/results/report data is exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
