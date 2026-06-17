---
module: Reports & Analytics
total_user_stories: 1
total_test_cases: 16
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
