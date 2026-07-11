---
id: TC-RPT-ISO-007
user_story: US-RPT-002
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-007: EF query filter constrains all leave/attendance aggregation paths incl. views; RLS deferred (AC-5, NFR-2)

## 1. Test Objective
Verify that EVERY aggregation path feeding the leave/attendance reports — leave requests/balances,
attendance logs, shift configs, and any PostgreSQL view/materialized view used for optimization — is
constrained to the resolved tenant by the EF Core global query filter, so no aggregation can sum
across tenants. RLS is documented as deferred defense-in-depth.

> PLATFORM NOTE / CONDITIONAL: The active isolation layer is the EF Core global query filter
> (`TenantId == _tenantContext.TenantId`) + `TenantInterceptor` + `ITenantContext`. The
> "raw SQL without `app.current_tenant_id` -> zero rows" RLS expectation is CONDITIONAL/deferred. If
> attendance optimization uses PostgreSQL views (NFR-6), assert the view query is ALSO tenant-filtered
> (the view must not bypass the EF filter by reading base tables unscoped).

## 2. Related Requirements
- User Story: US-RPT-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant context), NFR-6 (PostgreSQL views)
- Non-Functional: NFR-2 (tenant isolation)

## 3. Preconditions
- Tenant A and Tenant B active with overlapping department/leave-type NAMES but distinct ids and data.
- `hrA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A leave-day total | 42 | |
| B leave-day total | 19 | must never be added to A's |
| shared dept name | "Engineering" | exists in both tenants, distinct ids |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate Leave Utilization | Total = 42 (A only); B's 19 never added even though dept name "Engineering" exists in both |
| 2 | As `hrA`, generate Attendance Summary and Overtime | Aggregates derive only from A's attendance logs/shifts (EF filter on each base table) |
| 3 | If attendance uses a PostgreSQL view (NFR-6), generate the view-backed report | View output is tenant-scoped to A — the view does not read base tables unfiltered (CONDITIONAL on views being used) |
| 4 | Inspect the generated SQL/query plan for each aggregation | Each carries the `TenantId = A` predicate (EF global filter); no unfiltered cross-tenant scan |
| 5 | Attempt the raw-SQL RLS expectation (query without tenant GUC) | CONDITIONAL/deferred — documented as future RLS hardening; today isolation is enforced at the EF/app layer |
| 6 | Confirm no aggregation sums A + B | A's totals are exactly A's seeded values; B contributes zero |

## 6. Postconditions
- All aggregation paths (incl. views) tenant-scoped via EF filter; RLS deferred and documented.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
