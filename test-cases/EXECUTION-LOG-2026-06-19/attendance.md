# Attendance — API-Layer QA Baseline (Execution Log)

- **Date:** 2026-06-19
- **Environment:** Running backend at `http://localhost:5000`, tenant `acme`
- **Controller:** `src/backend/HRM.Api/Controllers/AttendanceController.cs` (route base `/api/v1/attendance`)
- **Personas (pwd `Admin@123!`, subdomain `acme`):** Tenant Admin `tenantadmin@acme.test`, HR `hr@acme.test`, Manager `manager@acme.test`, Employee `employee@acme.test` (linked to Core HR `EMP-001`)
- **Method:** curl smoke against the live API. No source or TC files modified. Browser not used.
- **Scope note:** Sampled critical/high TCs in `test-cases/attendance/` (TC-ATT-001..005 + ISO series). Did not read all 154.

## Permission map observed (from controller)
| Capability | Permission | Endpoints |
|---|---|---|
| Self check-in/out/status | `Attendance.CheckIn` | clock-in, clock-out, status, overtime/my, overtime/pre-approval, late-early/my-score |
| Self regularization | `Attendance.Regularize.Self` | POST/GET regularizations |
| Team approvals/views | `Attendance.Approve.Team` | regularizations/pending + approve/reject/bulk, overtime/pending+approve/reject, late-early/report |
| Shift management | `Attendance.Shift.Manage` | shifts CRUD/clone/assign, employees/{id}/shift |
| Admin views/config | `Attendance.View.All` | summary/monthly*, late-policy, payroll-data, reconciliation, dashboard*, reports/* |
| Period lock | `Attendance.Lock.Manage` | period-lock POST/unlock |

> **Contract note (not a defect):** `Attendance.View.Own` and `Attendance.View.Team` appear in the spec/TCs but have **no dedicated endpoint** in `AttendanceController` — they are referenced only in a comment in `MyPayslipsController`. "View own attendance" in this build is served by `GET /status` + `GET /regularizations` (both `Attendance.CheckIn` / `Regularize.Self`). Flag to caller: TCs that assert a distinct `View.Own`/`View.Team` attendance-history endpoint are currently untestable against this API surface.

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| /attendance/status | Employee | GET | PASS | 200 | `isClockedIn:false`, `requireGeolocation:false` envelope ok |
| /attendance/clock-in (self) | Employee | POST | PASS | 201 | Created AttendanceLog, `clockIn` UTC set, `id` returned |
| /attendance/clock-out (self) | Employee | POST | PASS | 200 | Same log, `clockOut` set, `totalWorkMinutes` calculated |
| /attendance/regularizations (list own) | Employee | GET | PASS | 200 | Empty list → after submit returned the record |
| /attendance/regularizations (submit valid, past) | Employee | POST | PASS | 201 | PENDING record created, `MISSED_CLOCK_IN` |
| /attendance/overtime/my | Employee | GET | PASS | 200 | Empty list, envelope ok |
| /attendance/late-early/my-score | Employee | GET | PASS | 200 | Score DTO `{lateCount,allowedLates,earlyDepartureCount}` |
| reg submit — future date | Employee | POST | PASS | 400 | "The corrected time cannot be in the future." |
| reg submit — invalid type `BOGUS` | Employee | POST | PASS | 400 | "must be one of MISSED_CLOCK_IN, MISSED_CLOCK_OUT, MISSED_BOTH" |
| reg submit — invalid time `99:99` | Employee | POST | PASS | 400 | "must be a valid 24-hour time in HH:mm format" |
| reg submit — reason < 10 chars | Employee | POST | PASS | 400 | "The reason must be at least 10 characters." |
| /attendance/regularizations/pending | Manager | GET | PASS | 200 | `{items:[],totalCount:0}` |
| /attendance/overtime/pending | Manager | GET | PASS | 200 | `{items:[],totalCount:0}` |
| /attendance/shifts | HR | GET | PASS | 200 | Seeded "General Shift" 09:00–17:00 |
| /attendance/shifts | Tenant Admin | GET | PASS | 200 | Same list |
| /attendance/late-policy | Tenant Admin | GET | PASS | 200 | Policy DTO `{thresholdCount:3,deductionDays:0.5,...}` |
| /attendance/dashboard | Tenant Admin | GET | PASS | 200 | `{expectedHeadcount:1,clockedIn:1,attendancePercent:100}` |
| /attendance/dashboard/live-board | Tenant Admin | GET | PASS | 200 | Envelope ok |
| /attendance/late-early/report | Tenant Admin | GET | PASS | 200 | Envelope ok |
| **/attendance/summary/monthly?month=2026-06** | Tenant Admin | GET | **FAIL** | **500** | Generic "An unexpected error occurred." (see Findings #1) |
| **/attendance/reconciliation?month=2026-06** | Tenant Admin | GET | **FAIL** | **500** | Generic "An unexpected error occurred." (see Findings #1) |
| **/attendance/payroll-data?month=2026-06** | Tenant Admin | GET | **FAIL** | **500** | Generic "An unexpected error occurred." (see Findings #1) |
| /attendance/regularizations/pending | Employee | GET | PASS | 403 | Forbidden (needs Approve.Team) |
| /attendance/shifts | Employee | GET | PASS | 403 | Forbidden (needs Shift.Manage) |
| /attendance/shifts | Manager | GET | PASS | 403 | Forbidden — Manager lacks Shift.Manage |
| /attendance/late-policy | Employee | GET | PASS | 403 | Forbidden (needs View.All) |
| /attendance/late-policy | Manager | GET | PASS | 403 | Forbidden — Manager lacks View.All |
| /attendance/dashboard | Employee | GET | PASS | 403 | Forbidden (needs View.All) |
| /attendance/status (no token) | — | GET | PASS | 401 | Unauthorized |
| /attendance/clock-in (no `X-Tenant-Subdomain`) | Employee | POST | PASS | 400 | "Tenant context is not resolved." |

## Findings

### 1. DEFECT — three admin aggregate GETs return 500 (HTTP 500), all `Attendance.View.All`
- `GET /api/v1/attendance/summary/monthly?month=2026-06`
- `GET /api/v1/attendance/reconciliation?month=2026-06`
- `GET /api/v1/attendance/payroll-data?month=2026-06`

Reproduced with the **correct** `month=yyyy-MM` param (confirmed not a param-name issue — `ResolveMonth` parses `month`, and `?year=&month=6` correctly returns 400). All three fail consistently with the generic exception-handler 500; sibling `View.All` read endpoints (`dashboard`, `dashboard/live-board`, `late-early/report`, `late-policy`) all return 200, so it is **not** an authz or month-parse problem — the fault is inside these three query/service handlers.

**Server-side stack trace not capturable from disk:** the on-disk Serilog file `src/backend/HRM.Api/Logs/log-20260617.txt` is frozen at 2026-06-17 22:41 with zero `2026-06-19` entries — the live process is logging to console only (or a different sink), so the exact exception for today's calls could not be retrieved. **Circumstantial root-cause signal:** that same 06-17 log shows repeated `StackExchange.Redis.RedisConnectionException` on `HMSET`/`HMGET` against `localhost:6379` (Redis down). These three monthly-aggregate endpoints are the most likely Redis-cache consumers in the module, so a non-graceful Redis dependency is the leading hypothesis. **Recommend caller confirm by reading the live console log / the `GetMonthlySummaryQuery` / `GetAttendanceReconciliationQuery` / `GetPayrollDataQuery` handlers** for an unguarded cache call or an EF aggregation throwing on empty data.

Either way this is a genuine FAIL: an admin-facing read endpoint must not 500. If Redis is the cause, the handlers should degrade gracefully (cache-miss → compute) rather than surface a 500.

### 2. CONTRACT GAP (flag, not a code fix for QA) — no dedicated `View.Own` / `View.Team` attendance endpoint
See contract note above. TCs asserting a distinct attendance-history endpoint under `Attendance.View.Own`/`View.Team` cannot be executed against the current API; coverage is partially met via `/status` + `/regularizations`. Report to caller to reconcile the TCs vs. the implemented surface.

### 3. Tenant isolation — light, as scoped
Only `acme` + platform tenants seeded, so a true cross-tenant A→B leakage assertion was not exercised end-to-end. Negative tenant-context check passed: a request **without** `X-Tenant-Subdomain` is rejected 400 "Tenant context is not resolved." (TC-ATT-ISO-002 intent). Full ISO-001/003/004 (cross-tenant data, RLS, cache-key scoping) remain **BLOCKED** pending a second seeded tenant.

## AuthZ summary (clean)
Role gating behaves correctly: Employee → 403 on Approve.Team / Shift.Manage / View.All; **Manager → 403 on Shift.Manage and View.All** (Manager only holds CheckIn + Approve.Team); unauthenticated → 401; missing tenant → 400. No over-permissioning observed.
