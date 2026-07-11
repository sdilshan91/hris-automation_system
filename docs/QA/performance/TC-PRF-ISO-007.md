---
id: TC-PRF-ISO-007
user_story: US-PRF-002
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-007: Cross-tenant write block -- server-derived tenant_id, foreign goal_id/cycle_id rejected on self-assessment save/submit (NFR-2)

## 1. Test Objective
Verify NFR-2 on writes: when an employee saves or submits a self-assessment, `tenant_id` is derived server-side from the resolved tenant context (never from the request body), and any `goal_id`/`cycle_id`/`employee_id` referenced in the payload that belongs to a different tenant is rejected. A client cannot create a self-assessment row in or referencing another tenant.

## 2. Related Requirements
- User Story: US-PRF-002
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-6
- Data Requirements: S7 (FKs to goal + cycle; tenant_id column)

## 3. Preconditions
- acme employee Asha authenticated with `Performance.Read.Self`; window open.
- globex has its own goals/cycle (globex goal_id and cycle_id known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Body-injected tenant_id | globex tenant id | should be ignored |
| Foreign goal_id | a globex goal UUID | should be rejected |
| Foreign cycle_id | a globex cycle UUID | should be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha (acme), submit a self-assessment with a body-injected `tenant_id` = globex | The injected tenant_id is ignored; the persisted row carries `tenant_id` = acme (server-derived via TenantInterceptor). |
| 2 | As Asha, submit referencing a foreign `goal_id` belonging to globex | Rejected -- the goal is not visible/valid in acme's tenant context (global query filter); 400/404; no row created. |
| 3 | As Asha, submit with a foreign `cycle_id` belonging to globex | Rejected -- cycle not valid in acme; no row created. |
| 4 | As Asha, set `employee_id` in the body to another acme employee | Ignored/overridden by the server-derived caller identity (`Performance.Read.Self` = own only); the row binds to Asha, never the injected employee. |
| 5 | Inspect globex data after these attempts | No new self_assessment row appears in globex; no acme write leaked into globex; globex goal/cycle counts unchanged. |

## 6. Postconditions
- tenant_id is always server-derived; foreign goal/cycle/employee references are rejected; no cross-tenant write occurs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
