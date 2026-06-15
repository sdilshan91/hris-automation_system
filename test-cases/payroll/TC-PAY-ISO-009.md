---
id: TC-PAY-ISO-009
user_story: US-PAY-003
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-009: Tenant A's payroll run includes ONLY Tenant A employees; Tenant B is fully excluded throughout the compute pipeline (cross-tenant compute isolation)

## 1. Test Objective
Verify AC-7 / FR-3 / FR-8: tenant isolation holds throughout the ENTIRE payroll computation pipeline, not just at the API edge. When an HR Officer in Tenant A initiates a run, the worker (with `ITenantContext` restored from job args, FR-3) fetches and computes ONLY Tenant A's active employees; no Tenant B employee, attendance, leave, salary structure, or adjustment is read or included; and every `payroll_run` / `payroll_slip` / `payroll_slip_detail` row written carries Tenant A's `tenant_id`. Reads are filtered by EF Core global query filters; writes are stamped by `TenantInterceptor`. (US-PAY-003 AC-7/FR-3 say "RLS enforces tenant isolation throughout the pipeline"; this platform enforces via EF query filters + TenantInterceptor + the tenant-scoped job arg -- if Postgres RLS policies are later added on the payroll tables, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-003
- Acceptance Criteria: AC-7
- Functional Requirements: FR-3, FR-5, FR-8
- Data Requirements: S7 (tenant_id discriminator on payroll_run / payroll_slip / payroll_slip_detail)

## 3. Preconditions
- Tenant "acme" (A): 3 active employees with structures + finalized attendance for May 2026.
- Tenant "globex" (B): its own employees + an overlapping-month payroll context.
- HR with `Payroll.Run` authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | 3 employees, runs payroll |
| Tenant B | globex | must be excluded |
| Period | May 2026 | both tenants have data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, initiate the May 2026 run; let the worker complete. | Run created + processed under tenant_id=acme; job args carry acme tenant_id; ITenantContext=acme restored before any query (FR-3). |
| 2 | Verify employee selection. | Exactly 3 slips -- one per acme employee; ZERO globex employees fetched or processed (total_employees counts acme only). |
| 3 | Verify every written row's tenant_id. | All payroll_run / payroll_slip / payroll_slip_detail rows for this run have tenant_id=acme; no globex tenant_id appears. |
| 4 | Verify inputs are tenant-scoped. | Attendance, leave, salary structures, and adjustments read during compute are all acme's; no globex input leaks into any acme slip's math. |
| 5 | DB-level cross-check. | `SELECT ... FROM payroll_slip WHERE payroll_run_id = R` returns only acme rows. (If RLS exists, a session set to globex cannot read this run's rows.) |
| 6 | Run payroll independently in globex; compare. | globex run includes only globex employees; acme totals and globex totals are disjoint -- no cross-tenant bleed in either direction. |

## 6. Postconditions
- The payroll pipeline is tenant-isolated end to end; a tenant's run never reads or writes another tenant's data.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
