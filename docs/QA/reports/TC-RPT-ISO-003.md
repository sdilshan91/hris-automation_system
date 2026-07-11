---
id: TC-RPT-ISO-003
user_story: US-RPT-001
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-003: EF global query filter constrains all report aggregation queries; RLS deferred (AC-5, NFR-2)

## 1. Test Objective
Verify the EF Core global query filter (`TenantId == _tenantContext.TenantId`) constrains EVERY
report aggregation query path — headcount, turnover, demographics, joiners/leavers, department
distribution, employment-type — including any path backed by PostgreSQL views/materialized views
(FR-6). No report query may use `IgnoreQueryFilters()` to span tenants.

> PLATFORM NOTE: NFR-2 names PostgreSQL RLS as a defense-in-depth layer. RLS is NOT provisioned
> today; isolation is enforced by EF global query filters (read) + `TenantInterceptor` (write).
> The "raw SQL without app.current_tenant_id returns zero rows" RLS expectation is documented as
> CONDITIONAL/deferred (step 5).

## 2. Related Requirements
- User Story: US-RPT-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6 (views/materialized views), FR-7
- Non-Functional: NFR-2 (RLS + EF filters; RLS deferred)

## 3. Preconditions
- Tenant A and Tenant B active with distinct populations (as in TC-RPT-ISO-001).
- Access to the report query layer / SQL the report endpoints execute.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenant under test | Tenant A | _tenantContext.TenantId = A |
| materialized view | mv_headcount (or equivalent) | if used per FR-6 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With tenant context = A, execute each report aggregation | Generated SQL/EF query includes a `TenantId == A` predicate (directly or via the filtered view); only A's rows aggregated |
| 2 | Inspect the report query code paths for `IgnoreQueryFilters()` | None used in report aggregation paths (no deliberate cross-tenant bypass) |
| 3 | If reports use a PostgreSQL view/materialized view (FR-6), confirm it is tenant-filtered or queried through the filtered entity | Aggregation over the view is still tenant-scoped to A; the materialized view either carries tenant_id and is filtered, or is refreshed/queried per-tenant |
| 4 | Switch context to Tenant B and re-run the same aggregation | Returns only B's rows; A's rows never appear |
| 5 | (CONDITIONAL/deferred) Once RLS is provisioned: execute the raw aggregation SQL without setting `app.current_tenant_id` | Returns zero rows (RLS denies). Documented as deferred until RLS is added; today the EF filter is the enforced control |
| 6 | Verify writes during report-driven cache population (if any) are tenant-stamped by TenantInterceptor | Any cache/derived rows carry the correct tenant_id |

## 6. Postconditions
- Every report aggregation is tenant-constrained by EF filters; RLS step documented as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
