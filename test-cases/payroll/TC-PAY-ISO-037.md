---
id: TC-PAY-ISO-037
user_story: US-PAY-010
module: Payroll
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PAY-ISO-037: Cross-tenant READ isolation on attendance + leave fetch -- Tenant A's run consumes ZERO Tenant B attendance/leave records (AC-5, FR-8)

## 1. Test Objective
Verify AC-5 / FR-8: when the payroll engine fetches attendance and leave summaries for a run, only records matching the run's tenant_id are retrieved. Tenant A "acme" and Tenant B "globex" run identical periods with overlapping (even identically-named) employees; A's run and reconciliation report must contain no B attendance/leave data and vice versa. LOP/OT/encashment computed for A derive solely from A's records.

## 2. Related Requirements
- User Story: US-PAY-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-8
- NOTE: AC-5/FR-8 specify "RLS enforces isolation"; this platform enforces via EF Core global query filters + TenantInterceptor on the payroll tables and the attendance/leave entities consumed by the internal service. This TC describes the EF mechanism and notes Postgres RLS as an extension point.

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with a finalized May 2026 attendance period + approved leave, overlapping employee names (e.g. "John Smith" in both).
- HR users in each tenant with `Payroll.Run`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Period | May 2026 in BOTH tenants | overlap |
| Shared name | "John Smith" in A and B | name-collision probe |
| Surfaces | attendance fetch, leave fetch, reconciliation report, computed LOP/OT/encashment | full coverage |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, run May 2026 payroll; inspect the attendance summaries consumed. | Only acme attendance rows are fetched; globex's identically-named "John Smith" attendance is absent (AC-5, FR-8). |
| 2 | Inspect the leave summaries consumed for acme's run. | Only acme approved-leave records appear; no globex leave records (FR-2, FR-8). |
| 3 | Open acme's pre-payroll reconciliation report. | Rows reflect only acme employees/attendance/leave; no globex rows (FR-7, FR-8). |
| 4 | Verify acme's computed LOP/OT/encashment. | Values derive solely from acme's attendance/leave; no figure is influenced by globex data. |
| 5 | Repeat steps 1-4 as globex HR. | Symmetric -- globex consumes only globex attendance/leave; acme excluded everywhere. |
| 6 | Inspect the generated query for the attendance/leave fetch. | Each query carries `tenant_id == current tenant` (EF global filter / TenantInterceptor); no query omits the filter (FR-8). |

## 6. Postconditions
- The attendance/leave fetch + reconciliation are tenant-filtered at the query level; no cross-tenant data feeds any tenant's payroll computation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
