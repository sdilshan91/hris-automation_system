# API-Layer QA Baseline -- Reports & Analytics (US-RPT)

- **Date:** 2026-06-19
- **API base:** http://localhost:5000 (RUNNING)
- **Tenant:** acme (header X-Tenant-Subdomain: acme)
- **Auth:** POST /api/v1/auth/login, envelope {success, data:{accessToken}} -- all 4 personas authenticated OK
- **Controllers under test:** HrReportsController (api/v1/reports), DashboardController (api/v1/dashboard)
- **Method:** curl smoke against the live API; no source/TC files modified; browser not used.

## Endpoints discovered

| Route | Method | Permission |
|-------|--------|-----------|
| /api/v1/reports | GET | Reports.View |
| /api/v1/reports/{type}/generate?refresh= | POST | Reports.View |
| /api/v1/reports/{type}/export | POST | Reports.Export |
| /api/v1/reports/exports | GET | Reports.Export |
| /api/v1/reports/exports/{exportId}/download | GET | Reports.Export |
| /api/v1/dashboard/widgets?refresh= | GET | [Authorize] (any authenticated; widget SET is the authz boundary) |

**Report type keys (6 HR reports):** headcount, turnover, demographics, joiners-leavers, department-distribution, employment-type-breakdown.
The catalog GET /api/v1/reports returns **12** types (the 6 HR + 6 leave/attendance: leave-utilization, leave-balance, attendance-summary, absenteeism-trends, overtime-report, late-arrival-report) -- a unified cross-module report catalog. Not a defect.

**Role -> permission facts (from PermissionCatalog.DefaultPermissionsFor):**
- Reports.View: TenantAdmin, HRManager, **HROfficer**, Manager, Auditor.
- Reports.Export: TenantAdmin, HRManager, **Auditor** -- **NOT HROfficer, NOT Manager, NOT Employee**.
- The hr@acme.test persona maps to **HROfficer** (View-only, no Export).

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| GET /reports (catalog) | HR Officer | GET | PASS | 200 | Returns 12 report descriptors incl. all 6 HR types |
| POST /reports/headcount/generate | HR Officer | POST | PASS | 200 | success:true; Total Headcount=1 (acme-scoped) |
| POST /reports/turnover/generate | HR Officer | POST | PASS | 200 | success:true |
| POST /reports/demographics/generate | HR Officer | POST | PASS | 200 | success:true |
| POST /reports/joiners-leavers/generate | HR Officer | POST | PASS | 200 | success:true |
| POST /reports/department-distribution/generate | HR Officer | POST | PASS | 200 | success:true |
| POST /reports/employment-type-breakdown/generate | HR Officer | POST | PASS | 200 | success:true |
| POST /reports/not-a-report/generate (neg) | Tenant Admin | POST | PASS | 400 | "Unknown report type." |
| POST /reports/headcount/export csv | Tenant Admin | POST | PASS | 200 | status:Completed, rowCount:7, format:csv (sync render) |
| POST /reports/headcount/export xlsx | Tenant Admin | POST | PASS | 200 | status:Completed, format:xlsx |
| POST /reports/headcount/export pdf | Tenant Admin | POST | PASS | 200 | status:Completed, format:pdf |
| POST /reports/headcount/export docx (neg) | Tenant Admin | POST | PASS | 400 | unsupported_format: "Use csv, xlsx, or pdf." |
| GET /reports/exports (history) | Tenant Admin | GET | PASS | 200 | Lists completed exports w/ rowCount/fileSizeBytes/expiresAt |
| POST /reports/headcount/export (AuthZ) | HR Officer | POST | PASS | 403 | HROfficer lacks Reports.Export -- correct deny |
| GET /reports/exports (AuthZ) | HR Officer | GET | PASS | 403 | Same -- View-only role correctly blocked |
| POST /reports/.../export & /generate (AuthZ) | Employee | POST | PASS | 403 | Employee lacks Reports.View/Reports.Export -- correct deny |
| GET /reports (AuthZ) | Employee | GET | PASS | 403 | Employee lacks Reports.View -- correct deny |
| GET /dashboard/widgets | Tenant Admin | GET | PASS | 200 | role=hr, 8 HR widgets (headcount, open-positions, pending-leave, attendance-today, upcoming-birthdays, recent-joiners, onboarding-in-progress, turnover-rate) |
| GET /dashboard/widgets | HR Officer | GET | PASS | 200 | role=hr, same 8 HR widgets |
| GET /dashboard/widgets | Manager | GET | PASS | 200 | role=employee (no team/no employee record), 2 widgets (upcoming-holidays, pending-actions) |
| GET /dashboard/widgets | **Employee** | GET | **FAIL** | **500** | Deterministic (3/3, incl. ?refresh=true) "An unexpected error occurred." |
| Isolation: acme token + X-Tenant-Subdomain: globex | HR Officer | POST | PASS | 404 | "Workspace not found" -- cross-tenant context rejected pre-auth |
| Isolation: missing tenant header | HR Officer | POST | PASS | 400 | "No tenant context resolved." |
| Isolation: no bearer | -- | GET | PASS | 401 | Unauthenticated rejected |
| Isolation: report data scope | HR Officer | POST | PASS | 200 | headcount=1, only acme single employee + dept surfaced |

## Findings

### DEFECT-1 (FAIL, HIGH) -- GET /api/v1/dashboard/widgets returns 500 for the Employee persona
- **Route/Status:** GET /api/v1/dashboard/widgets -> **HTTP 500** (deterministic, 3/3 attempts, persists with ?refresh=true so it is not a stale-cache artifact).
- **Why it matters:** The dashboard is the **default landing page for every authenticated user** (US-RPT-005 section 10). The base "Employee" role -- the most common persona in any tenant -- gets a hard 500 on first load.
- **Diagnosis (narrowed, not root-caused -- no server stderr available):** Both manager@acme.test and employee@acme.test resolve to the **same "employee" role path** in DashboardService.GetWidgetsAsync and **both lack an Employee record** (/api/v1/employees/me -> 404 for each; me is null). Yet Manager returns 200 with 2 widgets (upcoming-holidays, pending-actions) while Employee 500s. The differentiator is therefore **not** the null me -- it is the Employee persona distinct permission/role set (it uniquely holds Payroll.ViewOwn, Leave.ViewOwn, Notifications.ViewOwn). One of the employee-only widget collaborators invoked in BuildEmployeeWidgetsAsync -- most likely LeaveBalanceWidgetAsync (_leaveDashboard.GetMyBalancesAsync) or RecentPayslipsWidgetAsync (_payslips.ListMineAsync) -- **throws** (rather than returning a graceful Result.Failure/null) when invoked by a user that has the own-data permission but no backing Employee/payroll/leave-balance row. The widget Add(...) loop does not swallow a thrown exception, so the whole request faults.
- **Recommendation to caller:** route to @backend-dev to (a) capture the server-side stack trace, and (b) make the per-widget builders in BuildEmployeeWidgetsAsync fail-soft (omit the widget) when the acting user has no Employee/payroll/leave context, mirroring the null-me guards already present in AttendanceThisMonthWidgetAsync. QA cannot fix source per the execution contract.
- **Caveat:** the acme seed data has only **1 employee record** and the test personas are mostly User-only (no Employee row), so this also reflects thin seed data; but a 500 (vs a graceful empty dashboard) is a code defect regardless of data.

### Non-defects confirmed (so the caller does not chase them)
- **HR Officer 403 on export** is **correct** -- HROfficer holds Reports.View only; Reports.Export is reserved for TenantAdmin/HRManager/Auditor by design.
- **Employee 403 on all /reports endpoints** is **correct** -- Employee holds neither Reports permission.
- **12-type catalog** is by design (unified HR + leave + attendance report catalog).
- **Tenant isolation** holds at every layer probed: unknown subdomain -> 404, missing tenant -> 400, no bearer -> 401, and report data is acme-scoped (no cross-tenant rows observed).

### Coverage gaps (BLOCKED / not executed)
- GET /reports/exports/{exportId}/download (file-bytes endpoint, 404/410 paths) **not executed** -- deferred; the export init + history path is proven, but the actual byte download + expiry/cross-tenant-404 cases remain untested this run.
- **Async export path** (status:Queued, Hangfire, FR-5/FR-10 429 throttle) not exercised -- all test exports rendered synchronously (rowCount:7 < 1000). The thin acme dataset cannot trigger the >1000-row async branch.
- Cross-tenant isolation tested only "light" (context rejection + single-tenant data scope); a true two-tenant data-leak A-vs-B comparison was not run (only acme is provisioned with a usable login here).
