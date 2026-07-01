---
id: TC-RPT-ISO-011
user_story: US-RPT-003
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-011: EF global query filter constrains every payroll aggregation path; RLS deferred (AC-5, NFR-2)

## 1. Test Objective
Verify that the EF Core global query filter on `TenantId` constrains EVERY payroll-report aggregation
path — Run Summary totals, department distribution, statutory monthly/YTD, bank advice, and CTC —
across payroll_slip / payroll_slip_detail / payroll_adjustment and any reporting view, so no
aggregation can sum or surface another tenant's salary rows. Validates AC-5, FR-8, NFR-2.

> PLATFORM ACCURACY / DEFERRED: read isolation today = EF global query filter
> (`TenantId == _tenantContext.TenantId`) + `TenantInterceptor` write stamping. The story's PostgreSQL
> RLS layer (NFR-2 "raw SQL without app.current_tenant_id -> zero rows") is DEFERRED defense-in-depth;
> step 5 is CONDITIONAL on RLS being provisioned. Any deliberate `IgnoreQueryFilters()` in payroll
> reporting must be justified (e.g. only the tenant lookup in resolution middleware).

## 2. Related Requirements
- User Story: US-RPT-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Non-Functional: NFR-2, NFR-6 (read replicas — same filter must apply)

## 3. Preconditions
- Tenant A and Tenant B with finalized payroll data in shared tables.
- Access to the EF query path / SQL the report service emits (integration test harness).
- `hrA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shared tables | payroll_slip, payroll_slip_detail, payroll_adjustment | tenant-stamped |
| filter predicate | TenantId == ITenantContext.TenantId | applied to all queries |
| RLS check | deferred / conditional | NFR-2 future hardening |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, run Run Summary; inspect the emitted SQL/EF query | Every payroll table access carries the TenantId predicate (Tenant A only) |
| 2 | Run Department Distribution + Statutory (monthly + YTD) | Aggregations (SUM/GROUP BY) constrained to Tenant A rows; Tenant B rows never summed |
| 3 | Run Bank Advice + CTC | Bank rows and employer-contribution sums scoped to Tenant A only |
| 4 | Grep the payroll reporting code for `IgnoreQueryFilters()` | None on tenant-scoped report queries; any use is justified + documented |
| 5 | (CONDITIONAL — RLS) Execute the report's raw SQL without `app.current_tenant_id` set | Returns ZERO rows once RLS is provisioned (deferred; record as N/A until then) |
| 6 | (NFR-6) If read replicas configured, repeat via the replica | The SAME tenant filter applies on the replica path |

## 6. Postconditions
- All payroll aggregation paths tenant-filtered via EF; RLS expectation recorded as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
