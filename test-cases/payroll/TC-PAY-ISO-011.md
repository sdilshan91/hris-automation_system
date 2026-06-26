---
id: TC-PAY-ISO-011
user_story: US-PAY-003
module: Payroll
priority: critical
type: security
status: fail
created: 2026-06-15
---

# TC-PAY-ISO-011: Cross-tenant payroll writes blocked; tenant_id is session/job-derived and cannot be overridden via request or job args

## 1. Test Objective
Verify AC-7 / FR-1 / FR-2 / FR-3 / FR-8: the tenant_id stamped on payroll_run / payroll_slip / payroll_slip_detail is derived from the resolved tenant context (and, in the worker, from the trusted job argument), NOT from client-supplied input. A body-injected `tenant_id` (pointing at another tenant) on the initiate request is ignored/rejected; a tampered Hangfire job arg cannot make Tenant A's worker write into Tenant B; and a run cannot reference a payslip/employee/structure belonging to a foreign tenant.

## 2. Related Requirements
- User Story: US-PAY-003
- Acceptance Criteria: AC-7
- Functional Requirements: FR-1, FR-2, FR-3, FR-8
- Data Requirements: S7 (TenantInterceptor stamps tenant_id on BaseEntity; job arg is the trusted tenant source in the worker)

## 3. Preconditions
- Tenant "acme" (A) and "globex" (B) both active with employees.
- HR with `Payroll.Run` authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Tenant A |
| Injected tenant_id | globex_id | attacker-supplied |
| Foreign refs | a globex employee_id | cross-tenant FK attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, `POST /api/v1/payroll/runs` with a body that includes `tenant_id: globex_id`. | The injected tenant_id is IGNORED; the created payroll_run is stamped tenant_id=acme (session-derived), not globex. |
| 2 | Verify the enqueued job arg. | Job carries acme's tenant_id (from the resolved context), not the body value. |
| 3 | Tamper the job arg to globex and re-enqueue (simulated attack). | The worker either rejects the mismatch or, because all reads/writes are filtered/stamped by the restored context, still cannot read acme inputs while writing globex rows -- no acme->globex data crossover is produced. |
| 4 | Attempt to make an acme run include a globex employee_id directly. | Rejected / yields no slip -- the foreign employee is invisible under acme's query filter; no payroll_slip with employee from globex under tenant_id=acme. |
| 5 | Verify all written rows. | Every payroll_run / payroll_slip / payroll_slip_detail row from this run has tenant_id=acme; zero rows stamped globex. |
| 6 | From globex, confirm no acme-originated rows appeared in globex. | globex sees none of acme's run output; no write leaked across the boundary. |

## 6. Postconditions
- Payroll writes are stamped by the trusted tenant context; client/job-arg injection cannot cross tenants.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
