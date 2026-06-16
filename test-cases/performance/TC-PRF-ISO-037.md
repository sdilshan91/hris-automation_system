---
id: TC-PRF-ISO-037
user_story: US-PRF-010
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-037: Recommendations (+budgets, approval chains, history, aggregates, compensation) in Tenant A invisible from Tenant B -- cross-tenant READ isolation, incl. by direct id (NFR-2)

## 1. Test Objective
Verify NFR-2: all recommendation data -- recommendations, recommendation budgets, approval chains/approval history, cross-cycle history, aggregate summaries, and (encrypted) compensation details -- is isolated per tenant. A user (HR/manager) authenticated in Tenant B can never read, list, or retrieve any Tenant A recommendation, budget, approval task, or aggregate, including by passing a Tenant A recommendation/budget/cycle id directly. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor; recommendation tables scoped by tenant_id).

> Note: US-PRF-010 NFR-2 / S7 specify PostgreSQL RLS on the recommendations table (`tenant_id = current_setting('app.current_tenant_id')`). This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added, extend Step 4 to assert isolation at the DB session level as defense-in-depth (same caveat as US-PRF-001..009).

## 2. Related Requirements
- User Story: US-PRF-010
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (recommendations, budgets, approver chain, history; tenant_id scoped)

## 3. Preconditions
- Tenant "acme" (Tenant A) has recommendations (promotion/bonus/increment), a budget, an in-flight approval chain, cross-cycle history, and aggregates; known recommendationId / budgetId / cycleId.
- Tenant "globex" (Tenant B) has its own recommendations + an HR Officer and a manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has recommendations |
| Tenant B | globex | its own data |
| Auth context | globex | Tenant B |
| acme ids | recommendationId, budgetId, cycleId | direct-id probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/recommendations?cycleId=...` and `.../recommendations/summary` as globex | Only globex recommendations/aggregates returned; ZERO acme data (NFR-2). |
| 3 | `GET .../recommendations/{acme_recommendationId}`, `.../budgets/{acme_budgetId}`, and the summary with acme cycleId | 404 / empty -- the global query filter excludes acme rows; never 200 with acme recommendation/budget/approval/aggregate data. |
| 4 | Verify at the DB level | `SELECT * FROM recommendations WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme recommendation/budget/approval rows. (If RLS exists, confirm a globex-set session cannot read acme rows even via a direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own recommendations/budgets/history; zero globex data. |

## 6. Postconditions
- No cross-tenant recommendation, budget, approval, history, aggregate, or compensation data is exposed via API or direct id. No cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
