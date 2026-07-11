---
id: TC-RPT-ISO-019
user_story: US-RPT-005
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-019: EF global query filter constrains every per-module composition path the DashboardService aggregates; PostgreSQL RLS deferred (AC-5, FR-8, NFR-3)

## 1. Test Objective
Verify that because DashboardService COMPOSES the existing per-module services (Core HR, Leave, Attendance,
Recruitment, Onboarding), tenant isolation holds across ALL of those aggregation paths via the EF Core global
query filter (`TenantId == _tenantContext.TenantId`) + TenantInterceptor (write stamping) + ITenantContext --
no composed widget can pull cross-tenant rows. PostgreSQL RLS (named in AC-5/NFR-3 as defense-in-depth) is
DEFERRED; the raw-SQL/RLS expectation is CONDITIONAL and does not fail the case. Validates AC-5, FR-8, NFR-3.

## 2. Related Requirements
- User Story: US-RPT-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8 (tenant_id scoping in every query)
- Non-Functional: NFR-3 (tenant isolation; RLS deferred -> EF filters)

## 3. Preconditions
- Tenant A and Tenant B with overlapping data in every module DashboardService touches.
- `hrA` authenticated in Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| modules composed | Core HR, Leave, Attendance, Recruitment, Onboarding | every aggregation path |
| isolation layers active | EF query filter + TenantInterceptor + ITenantContext | RLS deferred |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, load dashboard; trace the queries each widget composes | every per-module query carries the `TenantId == TenantA` global filter; no widget returns Tenant B rows (FR-8) |
| 2 | Verify headcount/recent-joiners (Core HR path) | employee aggregation filtered to Tenant A only |
| 3 | Verify pending_leave / leave_balance (Leave path) | leave aggregation filtered to Tenant A only |
| 4 | Verify attendance_rate / team_attendance (Attendance path) | attendance aggregation filtered to Tenant A only |
| 5 | Verify open_positions (Recruitment path) and onboarding_in_progress (Onboarding path) | both filtered to Tenant A only |
| 6 | Confirm no composition path uses `IgnoreQueryFilters()` for dashboard reads | the filter is never bypassed in DashboardService |
| 7 | (DEFERRED/CONDITIONAL) Run a raw SQL read without `app.current_tenant_id` set | RLS-zero-rows expectation is CONDITIONAL -- recorded PENDING (RLS not yet enabled); EF-filter isolation above is the binding assertion |

## 6. Postconditions
- All composed per-module aggregations are tenant-isolated via EF filters; RLS documented as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
