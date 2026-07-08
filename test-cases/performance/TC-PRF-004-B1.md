---
id: TC-PRF-004-B1
user_story: US-PRF-004
module: Performance Management
priority: medium
type: functional
status: draft
created: 2026-07-08
---

# TC-PRF-004-B1: Cycle rating-scales management endpoint — list/create the tenant's rating scales used by cycles; tenant-scoped; HR authz (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `cycle` service calls a `rating-scales` endpoint that has **no route anywhere** in the backend — cycles reference a rating scale but there is no API to manage the tenant's rating-scale catalog. This stub documents the intended verification. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built rating-scales management endpoint under performance cycles (`GET/POST .../performance/rating-scales`) that lists and creates the tenant's rating scales (e.g. name, min/max, per-point labels) consumed by appraisal cycles. Reads/writes are tenant-scoped (only acme scales visible; server-derived `tenant_id`), and management is authorized to HR (`Performance.SetGoal.All` / `Performance.Publish.All`) — an ordinary employee cannot create scales.

## 2. Related Requirements
- User Story: US-PRF-004
- Acceptance Criteria: AC-B1 (cycle rating-scales management)
- Functional Requirements: cycle configuration (rating scale referenced by 360/self/manager scoring); ties to US-PRF-002/003/005 rating inputs
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; HR Officer with cycle-management permission authenticated in acme.
- At least one pre-seeded rating scale for acme (e.g. "Standard 1-5").

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing scale | Standard 1-5 | seeded for acme |
| New scale | "Competency 1-4" (min 1, max 4, labelled) | to create |
| Non-HR user | employee (`Performance.Read.Self`) | must be denied create |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, `GET .../performance/rating-scales` | 200; returns acme's rating scales including "Standard 1-5"; no scales from other tenants. |
| 2 | As HR, `POST .../performance/rating-scales` with "Competency 1-4" (min 1, max 4, point labels) | 201; scale persisted `tenant_id`=acme; returned in a subsequent list. |
| 3 | Create with invalid bounds (e.g. min ≥ max, or duplicate name) | 400 validation error; nothing persisted. |
| 4 | As an ordinary employee, POST a new scale | 403 (management is HR-gated). |
| 5 | Confirm tenant scoping | An other-tenant HR listing rating-scales never sees acme's "Competency 1-4" (structurally covered by ISO suite). |
| 6 | (If update/delete are in scope) modify/remove a scale | Reflected in list; a scale in use by an active cycle is protected per BR (reject or soft-guard) — assert whichever the built endpoint implements. |

## 6. Postconditions
- The acme rating-scale catalog contains the created scale, scoped to acme; only HR can mutate it; validation blocks malformed scales.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
