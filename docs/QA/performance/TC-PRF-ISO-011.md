---
id: TC-PRF-ISO-011
user_story: US-PRF-003
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-011: Cross-tenant write block on manager-review submit -- server-derives tenant_id (body injection ignored); foreign employee_id / cycle_id / goal_id rejected

## 1. Test Objective
Verify NFR-2 write isolation on the new review table: when a manager submits a review, the persisted `tenant_id` is derived server-side from the resolved tenant context, NOT from any client-supplied body field. A request injecting a different `tenant_id` is ignored (the row is stamped with the caller's tenant by TenantInterceptor). A submit referencing an `employee_id`, `cycle_id`, or `goal_id` belonging to another tenant is rejected -- no cross-tenant review row is created.

## 2. Related Requirements
- User Story: US-PRF-003
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-7

## 3. Preconditions
- acme and globex active. acme manager Ravi reviewing acme employee Asha (acme cycle FY26-H1, acme goals).
- A globex employee id E_globex, globex cycle id C_globex, and globex goal id G_globex are known.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected tenant_id | globex's id | must be ignored |
| Foreign employee_id | E_globex | must be rejected |
| Foreign cycle_id | C_globex | must be rejected |
| Foreign goal_id | G_globex | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme Ravi, submit a valid acme review but inject `tenant_id` = globex in the body | The persisted row's tenant_id = acme (server-derived via TenantInterceptor); the injected value is ignored. No globex row appears. |
| 2 | As acme Ravi, submit a review with `employee_id` = E_globex | Rejected (404/403/validation) -- the foreign employee is not visible/assignable in acme; no row created. |
| 3 | Submit with `cycle_id` = C_globex | Rejected -- foreign cycle not resolvable in acme. |
| 4 | Submit a rating against `goal_id` = G_globex | Rejected -- foreign goal not resolvable in acme; nothing persisted. |
| 5 | Submit a fully-acme review | Succeeds (control); row stamped tenant_id=acme. |

## 6. Postconditions
- Review rows are always stamped with the caller's server-derived tenant_id; cross-tenant employee/cycle/goal references are rejected; no cross-tenant write occurs.

> Note: tenant stamping is enforced by the TenantInterceptor (SaveChanges) + EF global query filters, the platform mechanism in lieu of the RLS named in NFR-2/S7 (documented extension point).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
