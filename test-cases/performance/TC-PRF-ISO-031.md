---
id: TC-PRF-ISO-031
user_story: US-PRF-008
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-031: Cross-tenant WRITE block -- server-derived tenant_id on pip/objectives/checkpoints/escalation (no body injection); foreign employee_id/manager_id/mentor_id rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 on the write path: the tenant_id stamped on a new pip / pip_objective / pip_checkpoint / escalation record is DERIVED SERVER-SIDE from the resolved tenant context (TenantInterceptor), never accepted from the request body. A caller cannot create or move a PIP into another tenant by injecting a `tenant_id` in the payload. Furthermore, a PIP cannot reference a foreign-tenant employee_id, manager_id, or mentor_id -- such references are rejected.

## 2. Related Requirements
- User Story: US-PRF-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (PIP fields), FR-4 (checkpoints)
- Data Requirements: S7 (tenant_id stamped server-side)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have employees, managers, and mentors.
- HR "Nadia Khan" authenticated in acme with `Performance.Review.All`.
- A globex employee id, globex manager id, and globex mentor id are known to the test.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acting tenant | acme (Nadia) | server-derived tenant_id |
| Injected tenant_id | globex_id in body | must be IGNORED |
| Foreign employee_id | globex employee | must be rejected |
| Foreign manager/mentor id | globex | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Nadia, `POST .../performance/pips` with a body that INJECTS `tenant_id = globex_id` | The injected tenant_id is IGNORED; the created PIP is stamped with acme's tenant_id (server-derived via TenantInterceptor); never written under globex. |
| 2 | As Nadia, attempt to create a PIP whose `employee_id` is a GLOBEX employee | Rejected -- the foreign employee is not visible under acme's query filter; the PIP is not created (no cross-tenant binding). |
| 3 | As Nadia, attempt a PIP referencing a GLOBEX manager_id or mentor_id | Rejected -- foreign manager/mentor references are not resolvable in acme; rejected. |
| 4 | Record a checkpoint / confirm escalation with an injected tenant_id in the body | The injected tenant_id is ignored; the child rows inherit acme's tenant_id (TenantInterceptor). |
| 5 | Verify persistence | All written pip / pip_objectives / pip_checkpoints / escalation rows carry tenant_id = acme; zero rows landed under globex. |

## 6. Postconditions
- All PIP writes are tenant-stamped server-side; body tenant_id injection is ignored; foreign employee/manager/mentor references are rejected. No cross-tenant write occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
