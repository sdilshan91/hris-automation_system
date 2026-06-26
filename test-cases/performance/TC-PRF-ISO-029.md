---
id: TC-PRF-ISO-029
user_story: US-PRF-008
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-029: PIPs (+ objectives, checkpoints, escalation, history) in Tenant A invisible from Tenant B -- cross-tenant READ isolation, incl. by direct id (NFR-2)

## 1. Test Objective
Verify NFR-2: all PIP data -- the pip record, pip_objectives, pip_checkpoints, escalation records, acknowledgement + status history, and report artifacts -- is isolated per tenant. An HR Officer authenticated in Tenant B can never read, list, or retrieve any Tenant A PIP, including by passing a Tenant A PIP/objective/checkpoint id directly. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor; pip tables scoped by tenant_id).

> Note: US-PRF-008 NFR-2 / S7 specify PostgreSQL RLS on the pip / pip_objectives / pip_checkpoints tables. This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added, extend Step 4 to assert isolation at the DB session level as defense-in-depth (same caveat as US-PRF-001..007).

## 2. Related Requirements
- User Story: US-PRF-008
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (pip / pip_objectives / pip_checkpoints, tenant_id scoped)

## 3. Preconditions
- Tenant "acme" (Tenant A) has a PIP for Sam Lee with objectives, checkpoints, an escalation record, and history; known pipId / objectiveId / checkpointId.
- Tenant "globex" (Tenant B) has its own PIPs and an HR Officer with `Performance.Review.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Sam's PIP |
| Tenant B | globex | its own PIPs |
| Auth context | globex | Tenant B |
| acme ids | pipId, objectiveId, checkpointId | direct-id probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/pips` as globex | Only globex PIPs returned; ZERO acme PIPs (NFR-2). |
| 3 | `GET .../performance/pips/{acme_pipId}` and `.../checkpoints/{acme_checkpointId}` + the report endpoint using acme ids | 404 / empty -- the global query filter excludes acme rows; never 200 with acme PIP/objective/checkpoint/escalation data. |
| 4 | Verify at the DB level | `SELECT * FROM pip WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme pip/objective/checkpoint rows. (If RLS exists, confirm a globex-set session cannot read acme rows even via a direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own PIPs; zero globex data. |

## 6. Postconditions
- No cross-tenant PIP, objective, checkpoint, escalation, history, or report data is exposed via API or direct id. No cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
