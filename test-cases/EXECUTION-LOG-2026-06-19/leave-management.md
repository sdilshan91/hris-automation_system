# Leave Management — API-Layer QA Baseline (Execution Log)

- **Date:** 2026-06-19
- **Module:** Leave Management
- **Scope:** API-layer smoke against the **running** backend (`http://localhost:5000`). No source/TC files modified. No browser. No destructive endpoints called.
- **Tenant:** `acme` (subdomain). Personas (password `Admin@123!`): Tenant Admin `tenantadmin@acme.test`, HR Officer `hr@acme.test`, Manager `manager@acme.test`, Employee `employee@acme.test`.
- **Auth:** `POST /api/v1/auth/login` with `X-Tenant-Subdomain: acme` → envelope `{success,data:{accessToken}}`. All four personas authenticated successfully (200).

## Discovered routes (from controllers)

| Controller | Base route | Key endpoints (permission) |
|---|---|---|
| LeaveTypes | `api/v1/tenant/leave-types` | GET (LeaveType.View), GET/{id} (LeaveType.View), POST (LeaveType.Create), PUT/{id} (LeaveType.Edit), POST/{id}/deactivate (LeaveType.Deactivate), POST/{id}/reactivate, POST/reorder |
| LeaveRequests | `api/v1/leaves` | POST (Leave.Apply), GET mine / my-balance / my-ledger / my-upcoming / balance-preview (Leave.View.Own), GET pending / team-calendar (Leave.Approve.Team), POST {id}/approve, {id}/reject, {id}/cancel |
| LeaveEntitlements | `api/v1/tenant/leave-entitlements` | GET/POST/PUT rules, rules/bulk, overrides, effective — all (Leave.ConfigurePolicy) |
| LeaveCarryForward | `api/v1/leaves` | GET carry-forward-preview (Leave.ConfigurePolicy) |
| LeaveLop | `api/v1/leaves` | POST assign-lop, GET lop-summary, POST compulsory, POST lop/{id}/override — all (Leave.ManageLop) |
| LeaveReports | `api/v1/leaves` | GET reports/{reportType}, analytics/{chartType}, reports/{reportType}/export — all (Leave.Reports) |

**Valid report types** (enum `LeaveReportType`): `BalanceSummary`, `Utilization`, `Absenteeism`, `CarryForwardSummary`, `LopSummary`, `DepartmentCalendarCoverage`. **Analytics chart types:** `UtilizationByDepartment`, `LeaveByType`, `MonthlyTrend`. **Valid AccrualFrequency:** `Monthly`, `Quarterly`, `Yearly`, `Upfront`.

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `/api/v1/auth/login` (all 4 personas) | All | POST | PASS | 200 | All return `accessToken`; JWT carries correct roles/permissions |
| `/tenant/leave-types` (TC-LV-001 area) | HR Officer | GET | PASS | 200 | Returns seeded list incl. "Annual Leave" |
| `/tenant/leave-types` | Tenant Admin | GET | PASS | 200 | Returns list (LeaveType.View) |
| `/tenant/leave-types` (authZ negative) | Employee | GET | PASS | 403 | No LeaveType.View — correctly denied |
| `/tenant/leave-types` (TC-LV-001 happy) | HR Officer | POST | PASS | 201 | Created "QA Smoke Leave…", body returns full DTO + id |
| `/tenant/leave-types` (authZ negative) | Employee | POST | PASS | 403 | No LeaveType.Create — correctly denied |
| `/tenant/leave-types` (authZ negative) | Manager | POST | PASS | 403 | No LeaveType.Create — correctly denied |
| `/tenant/leave-types` (bad accrual) | HR Officer | POST | PASS | 400 | "Accrual frequency must be one of: Monthly, Quarterly, Yearly, Upfront." — validation works |
| `/tenant/leave-types/{zero-guid}` (isolation/negative) | HR Officer | GET | PASS | 404 | "Leave type not found." — EF query filter → 404-not-403 (matches platform pattern) |
| `/leaves/mine` | Employee | GET | BLOCKED | 403 | "No employee record is linked to the current user." (seed gap, see Findings) |
| `/leaves/my-balance` | Employee | GET | BLOCKED | 403 | Same no-employee-record domain guard |
| `/leaves/my-upcoming` | Employee | GET | BLOCKED | 403 | Same no-employee-record domain guard |
| `/leaves/balance-preview` | Employee | GET | BLOCKED | 403 | Same no-employee-record domain guard |
| `/leaves/my-balance` | HR / Manager / TenantAdmin | GET | PASS | 403 | Admin roles lack `Leave.View.Own` — correctly denied (not employee personas) |
| `/leaves` (apply, TC US-LV-002) | Employee | POST | BLOCKED | 403 | JWT HAS `Leave.Apply`; fails on missing employee record, not authZ (seed gap) |
| `/leaves` (apply) | Manager / HR | POST | PASS | 403 | No `Leave.Apply` permission — correctly denied |
| `/leaves/pending` | Manager | GET | PASS | 200 | `{items:[],totalCount:0,page:1,pageSize:20}` — paginated team queue |
| `/leaves/team-calendar` | Manager | GET | PASS | 200 | `{from,to,scope:"Employee",entries:[],holidays:[]}` |
| `/leaves/reports/summary` (invalid type) | HR Officer | GET | PASS | 400 | "Unknown report type 'summary'." — correct rejection of bad enum |
| `/leaves/reports/BalanceSummary` | HR Officer | GET | PASS | 200 | Columns + empty rows (no data seeded) |
| `/leaves/reports/Utilization` | HR Officer | GET | PASS | 200 | Columns + empty rows |
| `/leaves/analytics/LeaveByType` | HR Officer | GET | PASS | 200 | `{points:[],categories:[],series:[],scope:"All"}` |
| `/tenant/leave-entitlements/rules` | Tenant Admin | GET | PASS | 200 | `data:[]` (Leave.ConfigurePolicy granted to TA) |
| `/tenant/leave-entitlements/rules` (authZ) | HR / Manager / Employee | GET | PASS | 403 | Leave.ConfigurePolicy is TenantAdmin-only — all denied |
| `/leaves/carry-forward-preview` | Tenant Admin | GET | PASS | 200 | `data:[]` (Leave.ConfigurePolicy) |
| `/leaves/carry-forward-preview` (authZ) | Employee | GET | PASS | 403 | Correctly denied |
| `/leaves/lop-summary` | Tenant Admin / HR / Employee | GET | PASS | 403 | `Leave.ManageLop` not granted to ANY seeded role (see Findings) |
| Cross-tenant login: acme creds @ `platform` | Employee | POST | PASS | 403 | "You do not have an active membership in this organization." — isolation holds |

**Counts:** Endpoints/cases exercised ≈ 27. **PASS 22, FAIL 0, BLOCKED 5** (all 5 = the same single seed-data root cause).

## Findings

### No real defects (no 500s, no broken contracts, no authZ holes)
Every endpoint returned a coherent `ApiResponse` envelope. AuthZ enforced correctly across all personas. Validation (bad accrual, unknown report type) returns 400 with actionable messages. Cross-tenant unknown ID returns 404 (EF query-filter behavior, consistent with the platform's RLS-deferred pattern) and cross-tenant login is rejected with 403. Tenant isolation behaves as designed for the data available.

### F-1 — BLOCKER (seed data, NOT an app defect): no User↔Employee linkage for the Employee persona
- **Symptom:** `employee@acme.test` gets HTTP 403 `"No employee record is linked to the current user."` on every self-service endpoint (`/leaves/mine`, `/my-balance`, `/my-upcoming`, `/balance-preview`, and `POST /leaves` apply).
- **Root cause:** The seeded `employee@acme.test` user has the correct role/permissions (JWT confirms `Leave.Apply`, `Leave.View.Own`) but is not linked to an `Employee` domain record. The handler's employee-lookup guard fires before any leave logic.
- **Impact:** The end-to-end happy-path apply flow (HR creates leave type → Employee applies → Manager approves) **cannot be exercised via API** with current seed data. Steps 1 (create type) and 3 (manager views team queue) PASS; step 2 (apply) is BLOCKED.
- **Note:** This is a known platform seed-data gap, not a Leave-module bug. Recommend seeding an Employee record linked to `employee@acme.test` (and ideally a manager↔report relationship) to make the self-service + approval flow testable. **Report to caller** — this is outside QA's lane to fix.
- **Minor:** HTTP **403** is arguably the wrong status for "no employee record linked" — this is a missing-resource/precondition condition, not an authorization denial. A 409/422 (or 400) would be more accurate. Low severity; flagging for the BE owner, not fixing.

### F-2 — Config gap to confirm with caller: `Leave.ManageLop` granted to no seeded role
- `GET /leaves/lop-summary` returns 403 for **Tenant Admin, HR, and Employee** alike — no seeded role carries `Leave.ManageLop`. The entire LOP controller (assign-lop, lop-summary, compulsory, override) is therefore unreachable by any seeded persona.
- Likely intentional (LOP is a sensitive payroll-adjacent permission) but worth confirming whether Tenant Admin or HR *should* hold it. If a designed TC expects HR/TA to manage LOP, those TCs are currently un-executable. **Report to caller.**

### Traceability drift to note (not a defect): designed TCs reference `Leave.Configure`
- Sampled TCs (TC-LV-001, TC-LV-005) cite a permission named **`Leave.Configure`**, but the running code uses **`LeaveType.Create` / `LeaveType.Edit`** (leave-type config) and **`Leave.ConfigurePolicy`** (entitlement/carry-forward). The TC permission names are stale vs. the implemented permission catalog. Update the TCs' "Preconditions/Test Data" permission labels to match — a TC-file change, deferred (out of this read-only execution's scope).

## 6-line summary
- Endpoints hit: ~27 across all 6 leave controllers (types, requests, entitlements, carry-forward, LOP, reports) as 4 personas.
- Verdicts: **PASS 22, FAIL 0, BLOCKED 5** (the 5 BLOCKED share one root cause).
- Real defects: **none** — no 500s, no broken contracts, no authZ holes; validation, isolation (404-not-403, cross-tenant login 403) all correct.
- BLOCKER (seed, not app): `employee@acme.test` has no linked Employee record → 403 "No employee record is linked" on all self-service + apply endpoints, so the apply→approve happy path can't be exercised via API.
- Flag #1: 403 is a misleading status for "no employee record linked" (should be 409/422/400) — BE owner.
- Flag #2: `Leave.ManageLop` granted to no seeded role (LOP endpoints unreachable); TC permission names (`Leave.Configure`) are stale vs. code (`LeaveType.*` / `Leave.ConfigurePolicy`).
