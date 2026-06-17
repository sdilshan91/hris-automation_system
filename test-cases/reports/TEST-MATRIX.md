---
module: Reports & Analytics
total_user_stories: 4
total_test_cases: 64
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

---

## US-RPT-003 -- Payroll Reports & Summaries

> US-RPT-003 (Payroll Run Summary w/ MoM comparison, Department-wise Salary Distribution, Statutory
> Deductions monthly+YTD, Bank Advice w/ PII masking+reveal, Cost-to-Company) EXTENDS the existing
> payroll-reports surface (US-PAY-009, endpoints under `/api/v1/payroll/reports` +
> `PayrollReportsController`), NOT the generic `/api/v1/reports` surface. It adds 16 test cases: 12
> functional/security/performance/accessibility (TC-RPT-003-01..12) + 4 multi-tenant isolation
> (TC-RPT-ISO-009..012, continuing the running ISO counter from US-RPT-002's ISO-008). All 5
> acceptance criteria of US-RPT-003 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried forward from US-RPT-001/002 and prior modules):
> (1) AC-5 / NFR-2 specify PostgreSQL RLS, but this codebase isolates via **EF Core global query
> filters (read) + `TenantInterceptor` (write stamping) + `TenantResolutionMiddleware` -> scoped
> `ITenantContext`**, NOT Postgres RLS -- RLS is deferred defense-in-depth. ISO tests
> (TC-RPT-ISO-009..012) assert the EF mechanism in force today; the raw-SQL/RLS expectation is
> CONDITIONAL/deferred (TC-RPT-ISO-011 step 5); cross-tenant resource-ID injection (run_id/dept_id)
> asserts **404, not 403** (TC-RPT-003-10, TC-RPT-ISO-010). (2) FR-7 Redis report cache (key
> `t:{tenantId}:payroll-report:{type}:{paramsHash}`, TTL **15 min**) + repeat access: Redis is
> deferred dev-box infra -- TC-RPT-003-11 and TC-RPT-ISO-012 are CONDITIONAL (assert tenant-prefixed
> key shape + 15-min TTL + cache-hit on repeat + Refresh/invalidation), else assert
> identical-on-repeat + tenant-prefixed key derivation; the NFR-1 5s threshold is never relaxed to
> compensate for an absent cache. (3) NFR-1 (<5s P95 @ 5,000 emp) and NFR-6 (read replicas if
> configured) need a perf-representative environment (TC-RPT-003-11); on a dev box record indicative
> numbers and do NOT relax thresholds.
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller:
> (a) `Payroll.ViewSensitive` is a NEW permission introduced by this story. `PermissionCatalog.cs`
> today defines only `Payroll.View / .View.Own / .Run / .Approve / .Configure / .Export`, and EVERY
> current payroll-report endpoint (list, generate, analytics, bank-advice preview, export) is gated on
> `Payroll.Export`. The reveal/full-account behavior (TC-RPT-003-06/-07) requires the new permission
> to be ADDED to the catalog and the reveal endpoint + audit hook wired -- permission-granularity gap
> flagged. (b) Today bank-advice masking is split across endpoints: the `/reports/bank-advice/preview`
> endpoint masks (last 4) and the `/reports/{reportType}/export` (BankAdvice) endpoint emits FULL
> accounts -- both currently behind `Payroll.Export` only. US-RPT-003 adds an IN-UI reveal toggle +
> the new permission + the audit action; the existing full-export path must be re-gated behind
> `Payroll.ViewSensitive` to satisfy FR-6/NFR-3. (c) Audit action **"PayrollReport.ViewSensitive"**
> (NFR-3) is a NEW audit action (depends on US-NTF-004 audit trail); tests assert the EXACT string.
> (d) US-RPT-004 export is a separate story; export format/mechanics referenced as a dependency
> (TC-RPT-003-07 asserts the affordance + tenant format BR-4, not the full export engine).

### Coverage by Test Case (US-RPT-003)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-RPT-003-01 | Payroll Run Summary Mar 2026; gross/statutory-vs-voluntary/net/count = sum of payslips | E2E | Critical | AC-1, FR-1/2/5/8, BR-1/2 | Happy path |
| TC-RPT-003-02 | Run Summary MoM comparison; variance increase=red / decrease=green | Functional | Critical | AC-1, FR-3/5, BR-1/2 | Happy / boundary |
| TC-RPT-003-03 | Department-wise Salary Distribution stacked bar (basic/HRA/allowances) + per-dept totals & counts | Functional | Critical | AC-2, FR-1/2/5, BR-1/2 | Happy path |
| TC-RPT-003-04 | Statutory Deductions monthly + YTD cumulative (BR-5); match payslip deductions | Functional | Critical | AC-3, FR-1/2, BR-1/3/5 | Happy / boundary |
| TC-RPT-003-05 | Bank Advice account masked by default (last 4); name/bank/account/IFSC/net columns | Security | Critical | AC-4, FR-1/4/6, BR-1/2/4, NFR-3 | Happy / security |
| TC-RPT-003-06 | Bank Advice reveal needs Payroll.ViewSensitive; audit action "PayrollReport.ViewSensitive" (exact) | Security | Critical | AC-4, FR-6, NFR-3 | Negative / security |
| TC-RPT-003-07 | Bank Advice export in tenant format (CSV/text); full accounts permission-gated + audited | Functional | High | AC-4, FR-1/6, BR-2/4, NFR-3 | Happy / negative / security |
| TC-RPT-003-08 | Cost-to-Company includes employer contributions on top of gross (BR-6) | Functional | High | AC-1, FR-1/2/5, BR-1/2/6 | Happy / boundary |
| TC-RPT-003-09 | Draft run excluded (BR-1); multiple runs per period selectable; default=latest (FR-4) | Functional | Critical | AC-1, AC-4, FR-1/4, BR-1 | Negative / boundary |
| TC-RPT-003-10 | Invalid report_type / period format; out-of-tenant dept/run -> not-found; 0/single/full-year boundaries | Functional | High | AC-1, AC-3, AC-5, FR-1/2/4, BR-1/5 | Negative / boundary / security |
| TC-RPT-003-11 | Generation P95 <5s @5,000 emp; Redis cache tenant-prefixed TTL 15min + repeat (conditional) | Performance | High | AC-1/2/3, FR-7, NFR-1/6 | Boundary / performance |
| TC-RPT-003-12 | Charts alt text + table; variance not color-only; keyboard; responsive 360px-4K KPI/table scroll | Accessibility | Medium | AC-1/2/3/4, FR-3/5/6, NFR-4/5 | Accessibility / cross-browser |
| TC-RPT-ISO-009 | Payroll reports Tenant A vs B show only own salary data; no leakage | Security | Critical | AC-5, FR-8, NFR-2/3, BR-1 | Multi-tenant isolation |
| TC-RPT-ISO-010 | No-tenant-context rejected; cross-tenant run/dept ID injection -> 404 (not 403); spoofed tenant_id ignored | Security | Critical | AC-5, FR-2/8, NFR-2 | Multi-tenant isolation |
| TC-RPT-ISO-011 | EF filter constrains every payroll aggregation path (slip/detail/adjustment/views); RLS deferred | Security | Critical | AC-5, FR-8, NFR-2/6 | Multi-tenant isolation |
| TC-RPT-ISO-012 | Payroll report cache keys tenant-prefixed; no cross-tenant collision (Redis-conditional) | Security | High | AC-5, FR-7/8, NFR-2 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-RPT-003)

| AC | Covered By |
|----|-----------|
| AC-1 (Payroll Run Summary: total gross, deductions statutory-vs-voluntary, total net, employee count, MoM comparison chart + variance) | TC-RPT-003-01, -02, -08, -09, -10, -11, -12 |
| AC-2 (Department-wise Salary Distribution: stacked bar by component + per-dept totals/counts table) | TC-RPT-003-03, -11, -12 |
| AC-3 (Statutory Deductions: per-type monthly totals + YTD cumulative; downloadable) | TC-RPT-003-04, -10, -11, -12 |
| AC-4 (Bank Advice: name/bank/account/IFSC/net; masked default + permission-gated reveal; export in tenant format) | TC-RPT-003-05, -06, -07, -09, -12 |
| AC-5 (Tenant A vs B -- no cross-tenant payroll-data leakage; RLS + EF filters) | TC-RPT-003-10, TC-RPT-ISO-009, -010, -011, -012 |

### FR / NFR / BR Coverage (US-RPT-003)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (payroll reports: Run Summary, Dept Salary Distribution, Statutory, Bank Advice, CTC, ...) | TC-RPT-003-01, -03, -04, -05, -08 |
| FR-2 (filters: payroll period, department, location, pay grade, employment type) | TC-RPT-003-01, -03, -08, -10 |
| FR-3 (MoM comparison with variance highlighting: increase red / decrease green) | TC-RPT-003-02, -12 |
| FR-4 (multiple runs per period; select specific run; default latest) | TC-RPT-003-09, -10 |
| FR-5 (Chart.js/ngx-charts + data-table toggle) | TC-RPT-003-01, -02, -03, -08, -11, -12 |
| FR-6 (mask account numbers default last-4; Reveal toggle requires Payroll.ViewSensitive) | TC-RPT-003-05, -06, -07, -12 |
| FR-7 (Redis cache, TTL 15 min, tenant-prefixed key) | TC-RPT-003-11, TC-RPT-ISO-012 (Redis-conditional) |
| FR-8 (scope all payroll data by tenant_id from session) | TC-RPT-003-01, TC-RPT-ISO-009, -010, -011, -012 |
| NFR-1 (generation <= 5s P95 @ 5,000 emp) | TC-RPT-003-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-RPT-003-10, TC-RPT-ISO-009, -010, -011, -012 |
| NFR-3 (bank account/salary PII; access audited, action "PayrollReport.ViewSensitive") | TC-RPT-003-05, -06, -07, TC-RPT-ISO-009 |
| NFR-4 (responsive 360px-4K; KPI cards scroll, tables scroll, charts stack) | TC-RPT-003-12 |
| NFR-5 (WCAG 2.1 AA; variance not color-only; chart alt/table alternative) | TC-RPT-003-12 |
| NFR-6 (read replicas if configured) | TC-RPT-003-11, TC-RPT-ISO-011 (infra-conditional) |
| BR-1 (only finalized runs in reports; drafts excluded) | TC-RPT-003-01, -04, -08, -09, TC-RPT-ISO-009 |
| BR-2 (tenant currency symbol/decimal formatting) | TC-RPT-003-01, -02, -03, -08 |
| BR-3 (statutory categories per tenant jurisdiction) | TC-RPT-003-04 |
| BR-4 (bank advice follows tenant-configured format) | TC-RPT-003-05, -07 |
| BR-5 (year-end/statutory aggregates over tenant fiscal year) | TC-RPT-003-04, -10 |
| BR-6 (Cost-to-Company includes employer contributions in addition to gross) | TC-RPT-003-08 |

---

## US-RPT-004 -- Export Reports to CSV / PDF / Excel

> US-RPT-004 adds export (CSV / Excel .xlsx / PDF) to the generic reports surface
> (`/api/v1/reports`). Implementation contract: `POST /api/v1/reports/{type}/export
> {format, filters, includeCharts}` -> `{exportId, status: Completed|Queued, rowCount, format}`;
> `GET /api/v1/reports/exports` (history); `GET /api/v1/reports/exports/{exportId}/download`
> (tenant-scoped blob). Sync < 1000 rows, async >= 1000 via Hangfire; SignalR notify on async
> complete; audit every export; 3-in-progress-per-user concurrency cap; 7-day retention purge.
> Adds 16 test cases: 12 functional/integration/security/performance/accessibility
> (TC-RPT-004-01..12) + 4 multi-tenant isolation (TC-RPT-ISO-013..016, continuing the running ISO
> counter from US-RPT-003's -012). All 5 acceptance criteria covered.
>
> DEFERRED / CONDITIONAL (flag to caller -- never relax a threshold to compensate):
> (1) **Charts-as-images in PDF (AC-3 / FR-4):** server-side chart-to-PNG (SkiaSharp / headless
> capture, S10) is DEFERRED. TC-RPT-004-03 step 6 is CONDITIONAL -- the PDF title + filters + data
> tables + pagination + tenant-name footer (BR-5) are in-scope and binding; the chart-image step is
> recorded pending if not wired, and its absence does NOT fail the case.
> (2) **Cryptographic signed URLs + 15-min expiry (FR-7 / NFR-4):** DEFERRED. What IS implemented is
> an AUTHENTICATED, tenant-scoped `/exports/{exportId}/download` endpoint + BR-3 7-day retention
> purge. TC-RPT-004-06 asserts the tenant-403 + retention behavior that exists; the signed-URL /
> 16-min-expiry-410 steps are CONDITIONAL (pending if not wired).
> (3) **PostgreSQL RLS (NFR-7):** DEFERRED defense-in-depth (consistent with the whole module).
> Isolation TCs assert EF global query filters + TenantInterceptor + ITenantContext; cross-tenant
> exportId injection asserts **404, not 403** (TC-RPT-ISO-014); the raw-SQL RLS expectation is
> CONDITIONAL (TC-RPT-ISO-016 step 6).
> (4) **Audit action string (FR-9):** TC-RPT-004-07 asserts the export audit action verbatim against
> the implementation's documented constant (e.g. `Report.Export`) -- confirm the exact value; do not
> accept a lowercased/arbitrary variant (consistent with US-RPT-003 `PayrollReport.ViewSensitive`).
> (5) **Reports.Export permission + View.Team/.All scope (BR-1):** `Reports.Export` exists in the
> catalog; the Team-vs-All scope split still depends on scoped permission variants not yet exposed
> (flagged in US-RPT-001) -- TC-RPT-ISO-015 asserts the export reuses the report view's CURRENT
> scoping and flags the gap rather than relaxing it.
> (6) Backend is forward-looking -- the generic reports export surface is to-be-built; these are
> acceptance criteria for the implementation.

### Coverage by Test Case (US-RPT-004)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-RPT-004-01 | Export dropdown shows CSV/Excel/PDF; selecting a format initiates export | E2E | Critical | AC-1, FR-1/5 | Happy path |
| TC-RPT-004-02 | Excel async (>=1000) Hangfire .xlsx via ClosedXML; header title+filters+timestamp; SignalR ready link | Integration | Critical | AC-2, FR-3/5/8, BR-6 | Happy path |
| TC-RPT-004-03 | PDF title+filters+tables+pagination+tenant footer (BR-5); chart-image DEFERRED | Integration | High | AC-3, FR-4/5, BR-5 | Happy / conditional |
| TC-RPT-004-04 | CSV inline (100 emp): UTF-8 BOM, comma, header row, RFC-4180 escaping | Integration | Critical | AC-4, FR-2/5 | Happy / boundary |
| TC-RPT-004-05 | Sync/async routing boundary: 999 sync (Completed), 1000 async (Queued) | Integration | Critical | AC-2/4, FR-5/8 | Boundary |
| TC-RPT-004-06 | Tenant-scoped download: Tenant B -> 403; signed-URL 15-min expiry DEFERRED | Security | Critical | AC-5, FR-6/7, NFR-3/4 | Negative / security / isolation |
| TC-RPT-004-07 | Audit every export: report type, filters, row count, format, actor; action verbatim | Security | Critical | AC-2/3/4, FR-9 | Security |
| TC-RPT-004-08 | Concurrency cap: 3 in-progress/user; 4th -> 429/queued/rejected w/ message | Integration | High | AC-2, FR-10 | Negative / boundary |
| TC-RPT-004-09 | 7-day retention purge (BR-3) + max 100k rows (BR-4) + Excel "Filters Applied" (BR-6) | Integration | High | AC-2/5, BR-3/4/6 | Negative / boundary |
| TC-RPT-004-10 | Negative: invalid format/report_type, missing/expired download, no Reports.Export -> 403 | Security | Critical | AC-1/5, FR-1 | Negative / security |
| TC-RPT-004-11 | Perf: sync CSV <2s (NFR-1); async <60s @50k (NFR-2); no viewer degradation (NFR-6) | Performance | Medium | AC-2/4, NFR-1/2/6 | Performance (conditional) |
| TC-RPT-004-12 | A11y: export button/menu keyboard + SR; overflow menu <768px; progress/ready announced | Accessibility | Medium | AC-1/2, NFR-5 | Accessibility / cross-browser |
| TC-RPT-ISO-013 | Export history + download isolated; A never sees/downloads B's exports | Security | Critical | AC-5, FR-6, NFR-3 | Multi-tenant isolation |
| TC-RPT-ISO-014 | No-tenant-context rejected; cross-tenant exportId injection -> 404 (not 403); spoof ignored | Security | Critical | AC-5, NFR-3 | Multi-tenant isolation |
| TC-RPT-ISO-015 | Export DATA tenant+permission scoped; sensitive masked; SignalR ready only to owner | Security | Critical | AC-5, FR-8, BR-1/2, NFR-3 | Multi-tenant isolation |
| TC-RPT-ISO-016 | Storage path + retention purge + concurrency cap tenant-isolated; RLS deferred | Security | High | AC-5, FR-6/10, BR-3, NFR-7 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-RPT-004)

| AC | Covered By |
|----|-----------|
| AC-1 (Export dropdown CSV/Excel/PDF; selecting a format initiates export) | TC-RPT-004-01, -10, -12 |
| AC-2 (Excel async via Hangfire/ClosedXML; header title+filters+timestamp; formatted table; SignalR ready link) | TC-RPT-004-02, -05, -07, -08, -09, -11, -12 |
| AC-3 (PDF branded: title, filters, data tables, pagination, tenant footer; charts-as-images DEFERRED) | TC-RPT-004-03 |
| AC-4 (CSV: UTF-8 BOM, comma, header row, RFC-4180 escaping; small inline / large async) | TC-RPT-004-04, -05, -07, -11 |
| AC-5 (tenant-scoped download -> Tenant B 403; tenant-isolated storage; signed-URL expiry DEFERRED) | TC-RPT-004-06, -10, TC-RPT-ISO-013, -014, -015, -016 |

### FR / NFR / BR Coverage (US-RPT-004)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (three formats: CSV, Excel .xlsx, PDF) | TC-RPT-004-01, -10 |
| FR-2 (CSV UTF-8 BOM, comma, RFC-4180 escaping) | TC-RPT-004-04 |
| FR-3 (Excel via ClosedXML: formatted headers, data, optional chart sheet) | TC-RPT-004-02 |
| FR-4 (PDF via QuestPDF: branding, metadata, tables, pagination; chart PNGs DEFERRED) | TC-RPT-004-03 |
| FR-5 (<1000 rows sync; >=1000 async via Hangfire) | TC-RPT-004-01, -02, -04, -05 |
| FR-6 (tenant-isolated storage path {tenantId}/exports/{type}/{yyyy}/{mm}/{file}) | TC-RPT-004-06, TC-RPT-ISO-013, -016 |
| FR-7 (signed download URL, configurable expiry -- DEFERRED) | TC-RPT-004-06 (conditional), TC-RPT-ISO-016 |
| FR-8 (SignalR notify when async export ready, with download link) | TC-RPT-004-02, -05, TC-RPT-ISO-015 |
| FR-9 (audit every export: report type, filters, row count, format, actor) | TC-RPT-004-07 |
| FR-10 (max 3 concurrent exports per user) | TC-RPT-004-08, TC-RPT-ISO-016 |
| NFR-1 (sync CSV <1000 rows within 2s) | TC-RPT-004-11 |
| NFR-2 (async exports within 60s up to 50,000 rows) | TC-RPT-004-11 |
| NFR-3 (tenant-isolated paths; cross-tenant -> 403) | TC-RPT-004-06, -07, TC-RPT-ISO-013, -014, -015 |
| NFR-4 (signed URLs expire within 15 min -- DEFERRED) | TC-RPT-004-06 (conditional) |
| NFR-5 (export button/selector responsive + WCAG 2.1 AA) | TC-RPT-004-12 |
| NFR-6 (exports use background jobs; no viewer degradation) | TC-RPT-004-02, -11 |
| NFR-7 (tenant isolation via PostgreSQL RLS -- DEFERRED -> EF filters) | TC-RPT-ISO-016 (conditional) |
| BR-1 (export respects report scoping View.All vs View.Team) | TC-RPT-ISO-015 |
| BR-2 (sensitive data masked without ViewSensitive) | TC-RPT-ISO-015 |
| BR-3 (export files retained 7 days then Hangfire-purged) | TC-RPT-004-06, -09, TC-RPT-ISO-016 |
| BR-4 (max 100,000 rows per export) | TC-RPT-004-09 |
| BR-5 (PDF "Generated by [Tenant Name] via HRM SaaS" footer + timestamp) | TC-RPT-004-03 |
| BR-6 (Excel "Filters Applied" header section) | TC-RPT-004-02, -09 |
