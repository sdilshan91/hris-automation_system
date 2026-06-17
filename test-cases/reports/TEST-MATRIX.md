---
module: Reports & Analytics
total_user_stories: 2
total_test_cases: 32
created: 2026-06-17
updated: 2026-06-17
status: in-progress
---

# Reports & Analytics -- Test Matrix

> US-RPT-001 (Pre-Built HR Reports -- Headcount, Turnover, Demographics, Joiners/Leavers, Department
> Distribution, Employment-Type Breakdown) is the FIRST Reports & Analytics story and establishes
> `test-cases/reports/` (dir + this TEST-MATRIX + the root Reports section in TRACEABILITY-MATRIX).
> It adds 16 test cases: 12 functional/integration/security/performance/accessibility
> (TC-RPT-001-01..12) + 4 dedicated multi-tenant isolation (TC-RPT-ISO-001..004). The Reports module
> adopts the per-story-suffix functional ID scheme used since Recruitment (TC-RPT-{NNN}-XX) with a
> separate running ISO counter (TC-RPT-ISO-NNN) starting at 001. All 5 acceptance criteria of
> US-RPT-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED (consistent with prior modules): (1) AC-5 / NFR-2 specify PostgreSQL
> RLS as a tenant-isolation layer; this codebase isolates via **EF Core global query filters (read)
> + `TenantInterceptor` (write stamping) + `TenantResolutionMiddleware` -> scoped `ITenantContext`**,
> NOT Postgres RLS -- RLS is deferred defense-in-depth. Isolation tests (TC-RPT-ISO-001..004) assert
> the EF mechanism in force today; the "raw SQL without app.current_tenant_id -> zero rows" RLS
> expectation is documented as CONDITIONAL/deferred (TC-RPT-ISO-003 step 5); cross-tenant resource-ID
> access asserts **404, not 403** (existence not disclosed, TC-RPT-ISO-002 step 4). (2) FR-5 Redis
> report cache (key `t:{tenantId}:report:{name}:{paramsHash}`, TTL 5-15 min) and FR-8 Refresh-bypass:
> Redis is a deferred infra item on the dev box -- TC-RPT-001-09 and TC-RPT-ISO-004 are written
> CONDITIONAL on Redis being wired (assert the tenant-prefixed key shape + params-sensitivity +
> Refresh bypass), else assert identical-results-on-repeat + Refresh-re-queries + the key-derivation
> includes the tenant prefix; the NFR-1 3s threshold is never relaxed to compensate for an absent
> cache. (3) NFR-1 (<3s P95 @ 5,000 emp), NFR-3 (chart render <1s @ 10,000 pts), FR-6
> (PostgreSQL views/materialized views), NFR-6 (read replicas optional) need a perf-representative
> environment (TC-RPT-001-11); on a dev box record indicative numbers and do NOT relax thresholds.
> (4) BR-3 average-headcount convention: the turnover rate denominator ("Average Headcount in
> Period") is not fully specified -- TC-RPT-001-03 anchors to the Test Hint 100-emp/10-terminated
> case and requires the test to assert against the implementation's documented convention
> (opening-headcount basis -> 10%; (start+end)/2 = 95 basis -> 10.53%) rather than accept an
> unexplained value.
>
> IMPLEMENTATION STATUS / STORY MISMATCH worth flagging to the caller: (a) This is a NET-NEW general
> HR-analytics capability -- the backend does NOT yet exist. `Reports.View` and `Reports.Export`
> permissions ARE present in `PermissionCatalog.cs`, and module-specific reports exist for Leave
> (`LeaveReportsController`), Payroll (`PayrollReportsController`), and Attendance (overtime/late-early
> + `ScheduledReportJob`), but there is no cross-module Headcount/Turnover/Demographics reporting
> service. These TCs are forward-looking acceptance criteria for the to-be-built feature. (b) AC-5 /
> NFR-2 name Postgres RLS as an ACTIVE isolation layer -- only the app (ITenantContext) + EF (query
> filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with
> Auth/Leave/Payroll/Admin/Onboarding/Notifications). (c) BR-2 references `Reports.View.Team` /
> `Reports.View.All`; the catalog today exposes a single `Reports.View` (+ `Reports.Export`). The
> Team-vs-All SCOPE split (TC-RPT-001-08) requires either scoped permission variants or a manager
> direct-reports data filter to be added -- flag the permission-granularity gap to the caller. (d)
> NFR-6 read replicas and FR-6 materialized-view refresh schedule (Hangfire) are optional/infra and
> environment-dependent (TC-RPT-001-11 step 6, conditional). (e) US-RPT-004 export is a separate
> story; export is referenced as a dependency, not tested here.

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-RPT-001-01 | Headcount Summary current month; total matches active-employee count | E2E | Critical | AC-1, AC-2, FR-1/2/3/4/7, BR-1/4 | Happy path |
| TC-RPT-001-02 | Department filter restricts report to that dept + sub-departments | Functional | High | AC-2, FR-2/1/4 | Happy path |
| TC-RPT-001-03 | Turnover rate = separations / avg headcount * 100 (100emp/10term = 10%) | Functional | Critical | AC-3, FR-1/3, BR-3/4 | Happy / boundary |
| TC-RPT-001-04 | Active-status classification (active|probation=active; term|resigned|contract_ended=separated) | Functional | High | AC-2, AC-3, BR-4 | Happy / boundary |
| TC-RPT-001-05 | Demographics age computed at report date, not current date | Functional | High | AC-4, FR-1/3, BR-5 | Happy / boundary |
| TC-RPT-001-06 | Invalid report_type + out-of-tenant department/location filters rejected | Functional | High | AC-1, AC-2, AC-5, FR-2/7 | Negative / boundary / security |
| TC-RPT-001-07 | Unauthorized user (no Reports.View) blocked 403; unauth 401 | Security | Critical | AC-1, AC-2, BR-2 | Negative / security |
| TC-RPT-001-08 | Manager Team-scope sees only direct reports vs HR Officer full-tenant | Security | High | AC-2, AC-3, FR-7, BR-2 | Negative / boundary / security |
| TC-RPT-001-09 | Repeat identical request from Redis cache; Refresh bypasses (Redis-conditional) | Integration | High | AC-2, FR-5/8/7 | Happy / boundary / performance |
| TC-RPT-001-10 | Date-range + empty-population boundaries (single day, fiscal year, 0 emp, inverted range) | Functional | High | AC-2, AC-3, AC-4, FR-2/3/4, BR-3/6 | Negative / boundary |
| TC-RPT-001-11 | Generation P95 <3s @ 5,000 emp; chart render <1s @ 10,000 pts (perf-conditional) | Performance | High | AC-2, AC-3, AC-4, FR-5/6, NFR-1/3/6 | Boundary / performance |
| TC-RPT-001-12 | Charts have alt text + table-view alternative; keyboard; responsive 360px-4K | Accessibility | Medium | AC-2, AC-3, AC-4, FR-3/4, NFR-4/5 | Accessibility / cross-browser |
| TC-RPT-ISO-001 | Same report in Tenant A vs B shows only own data; no leakage | Security | Critical | AC-5, FR-7, NFR-2 (EF), BR-1 | Multi-tenant isolation |
| TC-RPT-ISO-002 | No-tenant-context rejected; cross-tenant ID injection -> 404 (not 403); spoofed tenant_id ignored | Security | Critical | AC-5, FR-7/2, NFR-2 | Multi-tenant isolation |
| TC-RPT-ISO-003 | EF filter constrains all aggregation paths incl. views; RLS deferred | Security | Critical | AC-5, FR-6/7, NFR-2 | Multi-tenant isolation |
| TC-RPT-ISO-004 | Report cache keys tenant-prefixed; no cross-tenant cache collision (Redis-conditional) | Security | High | AC-5, FR-5/7, NFR-2 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-RPT-001)

| AC | Covered By |
|----|-----------|
| AC-1 (report catalog lists 6 pre-built reports with description/icon/Generate) | TC-RPT-001-01, -06, -07 |
| AC-2 (Headcount Summary w/ filters: total, active vs inactive, by employment type, by sub-dept bar chart; tenant-scoped) | TC-RPT-001-01, -02, -04, -06, -08, -09, -10, -11, -12 |
| AC-3 (Employee Turnover: separations, voluntary/involuntary count+%, monthly trend, by-dept bar, avg tenure) | TC-RPT-001-03, -04, -08, -10, -11, -12 |
| AC-4 (Demographics: gender pie, age histogram 5yr buckets, dept stacked bar, location dist, diversity) | TC-RPT-001-05, -10, -11, -12 |
| AC-5 (Tenant A vs Tenant B -- no cross-tenant leakage; RLS + EF filters) | TC-RPT-001-06, TC-RPT-ISO-001, -002, -003, -004 |

## FR / NFR / BR Coverage (US-RPT-001)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (6 pre-built reports) | TC-RPT-001-01, -02, -03, -05 |
| FR-2 (filters: date range, department hierarchy multi-select, location, employment type, status) | TC-RPT-001-02, -04, -06, -10, TC-RPT-ISO-002 |
| FR-3 (interactive charts: bar/line/pie/histogram) | TC-RPT-001-03, -05, -11, -12 |
| FR-4 (chart + tabular views, togglable) | TC-RPT-001-01, -02, -10, -12 |
| FR-5 (Redis cache, key t:{tenantId}:report:{name}:{paramsHash}, TTL 5-15min) | TC-RPT-001-09, TC-RPT-ISO-004 (Redis-conditional) |
| FR-6 (PostgreSQL views / materialized views for aggregations) | TC-RPT-001-11, TC-RPT-ISO-003 (infra-conditional) |
| FR-7 (tenant_id set in query context) | TC-RPT-001-01, -08, TC-RPT-ISO-001, -002, -003, -004 |
| FR-8 (Refresh bypasses cache) | TC-RPT-001-09, TC-RPT-ISO-004 |
| NFR-1 (generation <= 3s P95 @ 5,000 emp) | TC-RPT-001-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-RPT-001-06, TC-RPT-ISO-001, -002, -003, -004 |
| NFR-3 (chart render <= 1s @ 10,000 pts) | TC-RPT-001-11 |
| NFR-4 (responsive 360px-4K; charts resize) | TC-RPT-001-12 |
| NFR-5 (WCAG 2.1 AA: charts alt text + data-table alternatives) | TC-RPT-001-12 |
| NFR-6 (read replicas if configured) | TC-RPT-001-11 (CONDITIONAL on replica config) |
| BR-1 (reports read-only; no data mutation) | TC-RPT-001-01, TC-RPT-ISO-001 |
| BR-2 (Reports.View.All = full tenant; Reports.View.Team = direct reports only) | TC-RPT-001-07, -08 (permission-granularity gap flagged to caller) |
| BR-3 (turnover rate = separations / avg headcount * 100) | TC-RPT-001-03, -10 |
| BR-4 (active = active|probation; separated = terminated|resigned|contract_ended) | TC-RPT-001-01, -03, -04 |
| BR-5 (demographic age/gender computed at report date, not current date) | TC-RPT-001-05 |
| BR-6 (reports respect tenant fiscal-year start for annual comparisons) | TC-RPT-001-10 |

---

## US-RPT-002 -- Leave & Attendance Reports

> US-RPT-002 (Leave Utilization, Leave Balance, Attendance Summary, Overtime, Absenteeism Trends)
> adds 16 test cases: 12 functional/integration/security/performance/accessibility
> (TC-RPT-002-01..12) + 4 multi-tenant isolation (TC-RPT-ISO-005..008, continuing the running ISO
> counter from US-RPT-001's ISO-004). All 5 acceptance criteria of US-RPT-002 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried forward from US-RPT-001 and prior modules):
> (1) AC-5 / NFR-2 specify PostgreSQL RLS, but this codebase isolates via **EF Core global query
> filters (read) + `TenantInterceptor` (write) + `TenantResolutionMiddleware` -> scoped
> `ITenantContext`**, NOT Postgres RLS -- RLS is deferred defense-in-depth. ISO tests
> (TC-RPT-ISO-005..008) assert the EF mechanism; the raw-SQL/RLS expectation is CONDITIONAL/deferred
> (TC-RPT-ISO-007 step 5); cross-tenant resource-ID injection asserts **404, not 403**
> (TC-RPT-002-07, TC-RPT-ISO-006). (2) FR-7 Redis report cache (key
> `t:{tenantId}:report:{type}:{filterHash}`, TTL 5 min) + Refresh-bypass: Redis is deferred dev-box
> infra -- TC-RPT-002-11 and TC-RPT-ISO-008 are CONDITIONAL (assert key shape + filter-hash
> sensitivity + Refresh, else identical-on-repeat + Refresh-re-queries + tenant-prefixed key
> derivation); the NFR-1 3s threshold is never relaxed to compensate for an absent cache. (3) NFR-1
> (<3s P95 @ 5,000 emp), NFR-3 (charts <1s), NFR-6 (PostgreSQL views for attendance) need a
> perf-representative env (TC-RPT-002-11); on a dev box record indicative numbers and do NOT relax
> thresholds.
>
> STORY-SPECIFIC / MISMATCH worth flagging to the caller:
> (a) Backend NOT built for this cross-module capability -- module-specific reports exist for Leave
> (`LeaveReportsController`), Payroll, and Attendance (overtime/late-early + `ScheduledReportJob`),
> but the unified Leave-Utilization/Balance/Attendance-Summary/Overtime/Absenteeism reporting service
> in this story does not yet exist. These TCs are forward-looking acceptance criteria.
> (b) AC-4 / FR-8 require a `Reports.View.Team` vs `Reports.View.All` SCOPE split (manager direct
> reports via `ReportsToEmployeeId` vs full tenant), but the catalog today exposes only a single
> `Reports.View` (+ `Reports.Export`). Closing AC-4 (TC-RPT-002-09) needs scoped permission variants
> OR a manager direct-reports data filter to be ADDED -- permission-granularity gap flagged.
> (c) BR-2 working-days come from the tenant working calendar (holidays + per-shift weekly offs); BR-5
> leave-year start is configurable (calendar or custom fiscal). TC-RPT-002-10 exercises a custom fiscal
> start; if the working-calendar / fiscal-year config is not yet wired, those steps are CONDITIONAL.
> (d) US-RPT-004 export is a separate story (dependency, not tested here).

### Coverage by Test Case (US-RPT-002)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-RPT-002-01 | Leave Utilization Q1 2026; totals/avg-per-dept/top-10/donut match seeded data | E2E | Critical | AC-1, FR-1/2/3/5, BR-1/5 | Happy path |
| TC-RPT-002-02 | Leave Balance = entitlement+carryforward−consumed−pending; green/yellow/red bands | Functional | Critical | AC-2, FR-1/3, BR-1/5 | Happy / boundary |
| TC-RPT-002-03 | Attendance Summary; attendance rate = present/working×100 (18/20=90%); late/early | Functional | Critical | AC-3, FR-1/4/5, BR-2/3/4 | Happy path |
| TC-RPT-002-04 | Absenteeism counts only unauthorized absences; approved leave excluded (BR-4) | Functional | Critical | AC-3, FR-4, BR-2/4 | Boundary |
| TC-RPT-002-05 | Overtime Report = hours exceeding shift standard for 3 employees (BR-3) | Functional | High | AC-3, FR-1/2/3, BR-3 | Happy / boundary |
| TC-RPT-002-06 | Filters (dept/leave-type/employee/shift) apply; aggregate drill-down works | Functional | High | AC-1, AC-3, FR-2/6 | Happy / boundary |
| TC-RPT-002-07 | Invalid report_type + out-of-tenant dept/employee/leaveType/shift ids rejected (404 not 403) | Functional | High | AC-1, AC-5, FR-2/7, NFR-2 | Negative / boundary / security |
| TC-RPT-002-08 | Unauthorized (no Reports.View) blocked 403; unauthenticated 401 | Security | Critical | AC-1/2/3/4, FR-8, NFR-2 | Negative / security |
| TC-RPT-002-09 | Manager Team-scope (direct reports via ReportsToEmployeeId) vs HR full tenant | Security | Critical | AC-4, FR-7/8, BR-1 | Negative / boundary / security |
| TC-RPT-002-10 | Boundaries: 0 emp, single-day, full leave-year (BR-5), terminated incl. historical/excl. balance (BR-6) | Functional | High | AC-2, AC-3, FR-2/4, BR-5/6 | Negative / boundary |
| TC-RPT-002-11 | Generation P95 <3s @5,000 emp; Redis cache (tenant+type+filter-hash, TTL 5min) + Refresh | Performance | High | AC-1/2/3, FR-7, NFR-1/3/6 | Boundary / performance |
| TC-RPT-002-12 | Charts alt text + table alternative; keyboard; responsive 360px–4K sticky first column | Accessibility | Medium | AC-1/2/3, FR-5, NFR-4/5 | Accessibility / cross-browser |
| TC-RPT-ISO-005 | Leave/attendance report Tenant A vs B shows only own data; no leakage | Security | Critical | AC-5, FR-7, NFR-2, BR-1 | Multi-tenant isolation |
| TC-RPT-ISO-006 | No-tenant-context rejected; cross-tenant ID injection -> 404 (not 403); spoofed tenant_id ignored | Security | Critical | AC-5, FR-7/2, NFR-2 | Multi-tenant isolation |
| TC-RPT-ISO-007 | EF filter constrains all leave/attendance aggregation paths incl. views; RLS deferred | Security | Critical | AC-5, FR-7, NFR-2/6 | Multi-tenant isolation |
| TC-RPT-ISO-008 | Report cache keys tenant-prefixed; no cross-tenant cache collision (Redis-conditional) | Security | High | AC-5, FR-7, NFR-2 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-RPT-002)

| AC | Covered By |
|----|-----------|
| AC-1 (Leave Utilization: total by type bar, avg utilization per dept grouped bar, top-10 table, leave-type donut; tenant-scoped) | TC-RPT-002-01, -06, -07, -08, -11, -12 |
| AC-2 (Leave Balance per emp/type, color bands green>50% / yellow 25-50% / red<25%) | TC-RPT-002-02, -08, -10, -11, -12 |
| AC-3 (Attendance Summary: working days, attendance %, late/early, overtime by dept, absenteeism by dept) | TC-RPT-002-03, -04, -05, -06, -08, -10, -11, -12 |
| AC-4 (Manager Team-scope direct reports via Reports.View.Team vs HR full tenant Reports.View.All) | TC-RPT-002-08, -09 |
| AC-5 (Tenant A vs B no cross-tenant leakage; RLS + EF filters) | TC-RPT-002-07, TC-RPT-ISO-005, -006, -007, -008 |

### FR / NFR / BR Coverage (US-RPT-002)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (Leave Utilization, Leave Balance, Attendance Summary, Absenteeism, Overtime, Late Arrival reports) | TC-RPT-002-01, -02, -03, -05 |
| FR-2 (filters: date range, department multi-select, location, employee, leave type, shift) | TC-RPT-002-05, -06, -07, -10, TC-RPT-ISO-006 |
| FR-3 (consumption vs entitlement, % utilization) | TC-RPT-002-01, -02, -05 |
| FR-4 (attendance rate = present/working×100; absenteeism rate = absent/working×100) | TC-RPT-002-03, -04, -10 |
| FR-5 (Chart.js/ngx-charts + data-table toggle) | TC-RPT-002-01, -11, -12 |
| FR-6 (drill-down from aggregate to individual employees) | TC-RPT-002-06 |
| FR-7 (Redis cache TTL 5min, key tenant+report-type+filter-hash) | TC-RPT-002-11, TC-RPT-ISO-008 (Redis-conditional) |
| FR-8 (Reports.View.All full tenant / Reports.View.Team direct reports) | TC-RPT-002-08, -09 (permission-granularity gap flagged) |
| NFR-1 (generation <= 3s P95 @ 5,000 emp) | TC-RPT-002-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-RPT-002-07, -08, TC-RPT-ISO-005, -006, -007, -008 |
| NFR-3 (charts render <= 1s client-side) | TC-RPT-002-11, -12 |
| NFR-4 (responsive 360px-4K; sticky first column on mobile) | TC-RPT-002-12 |
| NFR-5 (WCAG 2.1 AA: charts alt text + data-table alternative; bands not color-only) | TC-RPT-002-12 |
| NFR-6 (PostgreSQL views for attendance optimization) | TC-RPT-002-11, TC-RPT-ISO-007 (infra-conditional) |
| BR-1 (balance = entitlement + carryforward − consumed − pending; reports read-only) | TC-RPT-002-02, TC-RPT-ISO-005 |
| BR-2 (working days from tenant working calendar: holidays + per-shift weekly offs) | TC-RPT-002-03, -04, -10 |
| BR-3 (overtime = attendance hours exceeding shift standard) | TC-RPT-002-03, -05 |
| BR-4 (absenteeism excludes approved leave; counts only unauthorized) | TC-RPT-002-03, -04 |
| BR-5 (reports respect tenant leave-year start: calendar or custom fiscal) | TC-RPT-002-01, -02, -10 |
| BR-6 (terminated employees included in historical reports, excluded from current balance) | TC-RPT-002-10 |
