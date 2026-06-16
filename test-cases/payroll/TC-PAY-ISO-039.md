---
id: TC-PAY-ISO-039
user_story: US-PAY-010
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-039: Cross-tenant write/compute block on integration -- encashment + LOP/OT enrichment stamped with server-derived tenant_id; injected tenant_id/employee_id ignored; A's run never enriches B's slips (AC-5, FR-8)

## 1. Test Objective
Verify AC-5 / FR-2 / FR-8: every write produced by the attendance/leave integration -- the leave-encashment earning adjustment, the lop_days / overtime_amount / leave_encashment_amount enrichment on payroll_slip, and the advisory attendance/leave lock -- is stamped with the server-derived tenant_id (TenantInterceptor), not a client-supplied one. A body-injected tenant_id or a foreign employee_id is ignored/rejected, so Tenant A's run can never write LOP/OT/encashment onto Tenant B's slips, nor lock Tenant B's attendance.

## 2. Related Requirements
- User Story: US-PAY-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2, FR-3, FR-5, FR-6, FR-8
- NOTE: enforcement is via EF Core TenantInterceptor (write-stamp) + global query filters; Postgres RLS noted as an extension point.

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex"; each with finalized attendance + employees with encashable balances + an in-progress/next payroll run.
- Valid acme JWT; known globex tenant_id, employee_id, and payroll_slip id.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Write surfaces | encashment adjustment, slip lop/OT/encashment enrichment, advisory lock | tenant-stamped |
| Injection probes | body tenant_id=globex, employee_id=globex, slip_id=globex | must be ignored/rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, trigger encashment with a body-injected `tenant_id = globex`. | The injected tenant_id is ignored; the encashment adjustment is stamped with acme's server-derived tenant_id (FR-5, FR-8). |
| 2 | As acme HR, attempt to post an encashment/LOP enrichment referencing a globex employee_id or globex payroll_slip id. | Rejected -- the foreign id does not resolve within acme; no globex slip is modified (FR-2/3/8). |
| 3 | Run acme payroll; inspect the enriched slips. | lop_days/overtime_amount/leave_encashment_amount are written only on acme slips, all carrying acme's tenant_id (FR-2/3). |
| 4 | Inspect globex's slips after acme's run. | Unchanged -- no acme-run write touched any globex slip (cross-tenant write blocked). |
| 5 | Trigger the advisory attendance/leave lock for acme's run. | The lock flag applies only to acme's period records; globex attendance/leave remain editable (FR-6, FR-8). |
| 6 | Inspect the persisted rows + write path. | Every integration write was tenant-stamped server-side via TenantInterceptor; no client-supplied tenant_id was honored (FR-8). |

## 6. Postconditions
- All integration writes (encashment, slip enrichment, advisory lock) are server-tenant-stamped; cross-tenant writes and id injection are blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
