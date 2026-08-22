---
name: reports-tc-conventions
description: TC ID scheme + platform-deviation rules for the Reports & Analytics module (US-RPT-*)
metadata:
  type: project
---

FIRST Reports & Analytics story is US-RPT-001 (Pre-Built HR Reports). It established
`test-cases/reports/` (dir + TEST-MATRIX + the root Reports section in TRACEABILITY-MATRIX).

ID scheme (ADOPTS the Recruitment/Payroll/Performance/Admin/Onboarding/Notifications scheme):
- Functional/integration/security/perf/a11y: `TC-RPT-{NNN}-XX` (suffix counter RESETS per story).
- Multi-tenant isolation: `TC-RPT-ISO-NNN` (separate RUNNING counter; US-RPT-001 used ISO-001..004).
- US-RPT-001 = 12 functional (TC-RPT-001-01..12) + 4 ISO (TC-RPT-ISO-001..004) = 16 TCs, all 5 ACs.
- US-RPT-002 (Leave & Attendance Reports) = 12 functional (TC-RPT-002-01..12) + 4 ISO
  (TC-RPT-ISO-005..008) = 16 TCs, all 5 ACs. Running ISO counter now at 008.
  US-RPT-002 specifics: AC-1 Leave Utilization (totals-by-type/avg-per-dept/top-10/donut); AC-2 Leave
  Balance BR-1 = entitlement+carryforward-consumed-pending with color bands >50%green/25-50%yellow/
  <25%red (50% is YELLOW, band inclusive); AC-3 Attendance rate = present/working*100 (FR-4),
  absenteeism BR-4 EXCLUDES approved leave (only unauthorized), overtime BR-3 = hours over shift
  standard; AC-4 Manager Team-scope via ReportsToEmployeeId (SAME Reports.View granularity gap as
  US-RPT-001 — single Reports.View today, no .Team/.All variants); BR-2 working days from tenant
  working calendar; BR-5 leave-year start (calendar/custom fiscal); BR-6 terminated INCLUDED in
  historical, EXCLUDED from current balance. FR-7 here is the Redis cache (tenant+type+filter-hash,
  TTL 5min) — note US-RPT-002 numbers it FR-7 whereas US-RPT-001 numbered the same cache FR-5.

3-matrix update rule (same as every prior module): per-TC files + module TEST-MATRIX.md +
root TRACEABILITY-MATRIX.md (forward + backward + AC-coverage tables + a trailing *Note*).

Root TRACEABILITY-MATRIX.md is huge (>256KB, ~5000 lines) — never Read whole; APPEND via a temp
file then `cat temp >> matrix && rm temp` (heredoc/echo risk CRLF + line-1 truncation). See
[[notifications-tc-conventions]].

Platform deviations to assert (carry forward, consistent with all prior modules):
- AC-5/NFR-2 say PostgreSQL RLS, but platform = EF Core global query filters + TenantInterceptor +
  TenantResolutionMiddleware->ITenantContext (RLS deferred). ISO tests assert EF mechanism; raw-SQL
  RLS expectation is CONDITIONAL/deferred; cross-tenant resource-ID injection asserts 404 NOT 403.
- FR-5 Redis report cache key `t:{tenantId}:report:{name}:{paramsHash}` + FR-8 Refresh-bypass:
  Redis is deferred dev-box infra -> write TC-RPT-001-09 and TC-RPT-ISO-004 CONDITIONAL (key shape +
  params-sensitivity + Refresh) else assert identical-on-repeat + Refresh-re-queries; never relax NFR-1 3s.
- NFR-1 (<3s P95 @5000 emp), NFR-3 (chart <1s @10000 pts), FR-6 (PG views/matviews), NFR-6 (read
  replicas) need a perf env -> record indicative numbers on dev box, don't relax thresholds.

Story-specific gotchas worth keeping:
- BR-3 turnover denominator ("Average Headcount in Period") is UNDER-SPECIFIED. Anchor TC to the
  Test-Hint 100-emp/10-terminated case but assert against the IMPLEMENTATION's documented convention:
  opening-headcount basis -> 10%; (start+end)/2 = 95 basis -> 10.53%. Don't accept an unexplained value.
- BR-4 status buckets: active|probation = ACTIVE; terminated|resigned|contract_ended = SEPARATED.
- BR-5: demographic age computed AT REPORT DATE (date_to), not current date — boundary test needs a
  DOB that crosses a 5-year-bucket boundary between a historical report date and today.

STORY MISMATCH flagged to caller (US-RPT-001):
- Backend NOT built yet — net-new cross-module HR analytics. `Reports.View`/`Reports.Export` perms
  EXIST in PermissionCatalog.cs, and Leave/Payroll/Attendance module-reports exist, but no
  Headcount/Turnover/Demographics service. TCs are forward-looking acceptance criteria.
- BR-2 references `Reports.View.Team` vs `Reports.View.All`, but catalog today exposes only a single
  `Reports.View` (+ Reports.Export). The Team-vs-All SCOPE split (TC-RPT-001-08) needs scoped
  permission variants OR a manager direct-reports data filter to be ADDED — permission-granularity gap.
- US-RPT-004 export is a separate story (dependency, not tested here).

US-RPT-003 = TC-RPT-003-01..12 + ISO-009..012; US-RPT-004 = TC-RPT-004-01..12 + ISO-013..016.

US-RPT-005 (Dashboard with KPI Widgets) is the FINAL story — Reports module now COMPLETE.
= TC-RPT-005-01..12 + ISO-017..020 = 16 TCs, all 5 ACs. Running ISO counter now at 020.
MODULE TOTALS: 5 stories, 80 TCs (60 functional + 20 ISO), 25/25 AC. TEST-MATRIX frontmatter
flipped to status: complete + a "Reports & Analytics -- Module Completion Summary" table added.
The root TRACEABILITY top forward table (which had ONLY listed US-RPT-001 + a 16-TC TOTAL) was
brought up to the full module total (80 TCs / 25 AC) when closing the module.
US-RPT-005 specifics:
- Single server-driven endpoint GET /api/v1/dashboard/widgets?refresh= returns
  {role:'hr'|'manager'|'employee', greetingName, generatedAt, widgets:[...]}; role DERIVED SERVER-SIDE
  (test the tampered ?role= is ignored — TC-RPT-005-06). Widget shape: {widgetKey,label,value,
  previousValue,trendDirection,trendPercentage,trendIsPositive,miniChart{sparkline|donut|progress},
  items[],linkUrl,linkFilters}. DashboardService COMPOSES existing per-module services (Core HR/Leave/
  Attendance/Recruitment/Onboarding) — ISO-019 asserts the EF filter holds across EVERY composed path.
- AC-1 HR set / AC-2 Manager team-scoped via ReportsToEmployeeId (team-size=8 hint) / AC-3 Employee
  personal (leave donut, attendance progress, onboarding, holidays, payslips link, pending-actions).
- AC-4 click-through = linkUrl + linkFilters (pending-leave -> /leave/requests?status=Pending).
- BR-2 trendIsPositive encodes BUSINESS meaning not arithmetic sign: headcount-up=green/positive,
  turnover-up=red/negative (TC-RPT-005-05). BR-3 pending-approvals only items ASSIGNED to logged-in
  user. BR-4 birthdays/anniversaries within next 7 days (inclusive boundary). BR-6 quick-actions cap 5.
- FR-4 Redis cache key now has a USER segment: t:{tenantId}:dashboard:{role}:{userId}:{widgetKey},
  TTL ~3min (2-5min) — ISO-020 asserts tenant+role+USER scoping (no cross-user collision); Redis-conditional.
- NFR-1 dashboard <=2s P95, NFR-2 per-widget <500ms P95 (never relax). NFR-4 grid 1/2/4 cols at
  360/768/1280/1920px.
- DEFERRED (write conditional, don't fail): BR-5 module-enablement has NO per-tenant module flag today
  (assume-all-on baseline, TC-RPT-005-10); general pending-tasks/quick-actions queues may not be wired
  (empty = valid zero-state, TC-RPT-005-09, not error). RLS still deferred (404-not-403).
