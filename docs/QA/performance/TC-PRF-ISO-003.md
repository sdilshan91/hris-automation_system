---
id: TC-PRF-ISO-003
user_story: US-PRF-001
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-003: Cross-tenant write block — tenant_id is server-derived; body-injected tenant_id and foreign employee/cycle IDs rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 write isolation: when creating/editing goals, `tenant_id` is stamped from the resolved tenant context (TenantInterceptor), never from the request body. A body-injected `tenant_id` for another tenant is ignored, and references to a foreign-tenant `employee_id` or `cycle_id` are rejected — a manager in Tenant A can never write a goal into Tenant B or attach to Tenant B's employee/cycle.

## 2. Related Requirements
- User Story: US-PRF-001
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1
- Data Requirements: S7 (tenant_id, employee_id, cycle_id)

## 3. Preconditions
- acme manager "Ravi" authenticated with `Performance.SetGoal.Team`; window OPEN; Asha is his direct report in acme.
- globex has employee "Devin" and cycle "GX-FY26" (foreign-tenant references).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected tenant_id | globex_tenant_id in the POST body | must be ignored |
| Foreign employee_id | globex Devin's id | must be rejected |
| Foreign cycle_id | globex GX-FY26 id | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ravi (acme), `POST .../goals` for Asha with a body field `tenant_id = globex_id` | 201, but the persisted goal's `tenant_id` = acme (server-derived); the injected globex id is ignored. The goal is NOT visible from globex. |
| 2 | As Ravi, `POST .../goals` referencing `employee_id = globex_Devin_id` | Rejected (400/404/403) — the employee is not in acme / not Ravi's report; no goal written. |
| 3 | As Ravi, `POST .../goals` referencing `cycle_id = globex_GX-FY26_id` | Rejected (400/404) — the cycle does not belong to acme; no goal written. |
| 4 | Authenticate in globex and list goals | None of the above attempts produced any goal in globex; Devin/GX-FY26 have no new goals. |

## 6. Postconditions
- Goal writes are confined to the authenticated tenant; no cross-tenant write, injection, or foreign-reference attachment succeeds.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
