---
title: QA Execution Baseline — "as delivered" run
created: 2026-06-19
status: in-progress
mode: agent-driven (Playwright MCP for UI, curl for API), prioritized critical + smoke
target: running local stack (FE http://localhost:4200, API http://localhost:5000)
---

# QA Execution Baseline (2026-06-19) — before any bug fixes

Purpose: record the **actual delivered state** of the system by executing prioritized test cases
against the running app, BEFORE fixing the bugs in [BUG-REPORT-2026-06-19.md](../BUG-REPORT-2026-06-19.md).
This is the "before" snapshot; re-run after fixes for the "after".

## Methodology
Every test case is executed at up to **two layers**, recorded separately, because the bugs are
frontend-only:
- **API layer** — direct HTTP to the backend with a real JWT (`admin@hrm.local`, holds all permissions).
  Tests backend delivery independent of the FE.
- **UI layer** — the running Angular app driven via Playwright MCP, as the relevant persona.

Verdicts: **PASS** / **FAIL** (real defect) / **BLOCKED** (cannot execute — missing seed data /
dependency) / **N/A** (layer not applicable) / **PENDING** (not yet run).

## 🔑 Headline finding so far
**The backend is permission-gated and works; the failures are a frontend authorization-wiring wall.**
The seeded `admin@hrm.local` can drive the System Admin Console **via API** (provision/list tenants,
plans, subdomain checks all 200) but **cannot reach any of it in the UI** — every admin/feature route
redirects to `/forbidden` (BUG-1/2/3). So: *backend delivered ≠ frontend usable*. Track the two layers
separately or the baseline will look worse (or better) than reality.

## Seed (done) — deterministic personas
Tenant `acme` provisioned via API (US-ADM-001 happy path → **201 PASS**; tenantId
`019edee2-dbbf-729f-8199-eddffcb33b7d`, status Trial). Persona users seeded via SQL (password hash
reused from the seeded admin → all share password `Admin@123!`). All verified login → **200**.

| Persona | Email | Tenant / subdomain | Password |
|---|---|---|---|
| Platform SystemAdmin | admin@hrm.local | platform | Admin@123! |
| Tenant Admin | tenantadmin@acme.test | acme | Admin@123! |
| HR Officer | hr@acme.test | acme | Admin@123! |
| Manager | manager@acme.test | acme | Admin@123! |
| Employee | employee@acme.test | acme | Admin@123! |

Dev tenant resolution: send header `X-Tenant-Subdomain: <platform|acme>`. Login: `POST /api/v1/auth/login`,
response envelope `{success, data:{accessToken,...}}`. **Cleanup later:** delete acme users + tenant `019edee2-…`.

**Browser constraint:** the Playwright MCP is ONE shared browser → UI-layer tests run sequentially;
API-layer execution is parallelized across modules.

Also seeded: a linked Core HR **Employee** record (`EMP-001`, Eve) for `employee@acme.test` — unblocks
self-service flows (`/leaves/mine` 403→**200**). Without it, self-service endpoints 403 `no_employee_record`.

## Running tally (API layer unless noted)
| Module | Checks | PASS | FAIL | BLOCKED | Notes |
|---|---|---|---|---|---|
| Authentication | 5 | 5 | 0 | 0 | API; UI login PASS |
| Admin Console — UI | 5 | 3 | 2 | 0 | UI 403 (BUG-1/2) |
| Admin Console — API | 23 | 23 | 0 | 0 | system+tenant; isolation CLEAN; 41 blocked-TCs = deferred infra |
| Core HR | 26 | 26 | 0 | 0 | 2 creates persisted; isolation holds |
| Leave | 27 | 22 | 0 | 5 | 5 blocked = employee-link (now seeded) |
| Payroll | 25 | 25 | 0 | 0 | sensitive-data gate works; no payroll run exercised |
| Performance | 22 | 20 | 0 | 1 | empty-state contract nit |
| Attendance | 29 | 25 | 3 | 1 | **3× 500** (monthly/reconciliation/payroll-data) |
| Recruitment | 24 | 23 | 0 | 1 | public+mgmt OK; 2 low findings |
| Onboarding | 24 | 22 | 0 | 2 | blocked = no checklist seeded |
| Reports | 24 | 23 | 1 | — | **1× 500** (Employee dashboard) |
| Notifications | 28 | 28 | 0 | 0 | clean; isolation fail-closed |
| **GRAND TOTAL** | **~262** | **~245** | **6** | **~10** | see defects below |

## ✅ Confirmed defects (independently reproduced)
| Bug | Sev | Endpoint | Status | Root cause |
|---|---|---|---|---|
| FE BUG-1/2/3 | 🔴/🟠 | all `/admin/*` + feature routes (UI) | 403/forbidden | role-string + persona/nav wiring (frontend) |
| **BUG-6** | 🔴 HIGH | `GET /dashboard/widgets` (Employee) | **500** | `DateTime.Kind=Unspecified`→timestamptz in `LeaveDashboardService:273` |
| **BUG-7** | 🔴 HIGH | `/attendance/{summary/monthly,reconciliation,payroll-data}` | **500×3** | same date-kind bug in `AttendanceSummaryService:349` |
| BUG-8 | 🟡 LOW | acme token under `admin` subdomain | 200 (no leak) | token tenant_id not cross-checked vs resolved tenant |

**BUG-6 + BUG-7 are one defect class** (constructed DateTime not UTC-kinded before Npgsql `timestamptz`).
Only surfaced because we seeded a REAL employee + real data — empty-data happy paths returned 200.

## Verdict (as-delivered)
- **Backend ~245/262 PASS, 0 isolation breaches, authZ correct.** Solid — but **2 HIGH 500 defects** on
  real-data employee/HR paths that unit tests + empty-data smoke never hit.
- **All UI failures are frontend auth-wiring** (BUG-1/2/3) — backend delivers what the UI can't reach.
- **UI testing limited to the platform persona** (dockerized FE hardcoded to `platform` subdomain);
  tenant-persona behavior validated at the API layer only.
- Designed-TC ↔ catalog **permission drift** is pervasive — feeds the "specs ≠ runnable" coverage finding.

Bugs are catalogued in [../BUG-REPORT-2026-06-19.md](../BUG-REPORT-2026-06-19.md) (BUG-6/7/8 addendum).
**Next:** fix BUG-6/7 (one date-kind class) + BUG-1, then re-run this baseline for the "after" snapshot.

## Cross-cutting findings (wave 1)
- **Backend API is solid** — 0 5xx across 124 API checks; authZ + query-filter tenant isolation correct.
- **Failures are frontend-only** (BUG-1/2/3) — backend delivers what the UI can't reach.
- **Permission/TC drift (TC-side, not code):** HR Officer has no payroll perms; `Leave.ManageLop` maps to
  no seeded role; `CustomField.View` is admin-only; several TCs cite stale permission names. Designed TCs
  often aren't executable as written → feeds the QA-coverage "specs ≠ runnable" finding.
- **Contract nits (low):** inconsistent empty-state (404 `no_cycle` vs 200 empty); 403 used for a
  precondition ("no employee linked" — should be 409/422); ~3 list-envelope shapes under `ApiResponse<T>`.
- **Env limitation:** the dockerized FE is built with `tenantSubdomain: 'platform'` → **UI testing of
  `acme` personas isn't possible without a FE rebuild/header override**; tenant-persona behavior is
  validated at the **API layer** only. Platform/SystemAdmin UI = the 403 wall (already characterized).
- **41 blocked designed TCs** (all in admin-console) = legitimate deferred infra (Postgres RLS, email
  delivery, OpenTelemetry), not defects.

## Module ledgers
- [authentication.md](./authentication.md) · [admin-console.md](./admin-console.md) (UI) · [admin-console-api.md](./admin-console-api.md)
- [core-hr.md](./core-hr.md) · [leave-management.md](./leave-management.md) · [payroll.md](./payroll.md) · [performance.md](./performance.md)
- _wave 2: attendance, recruitment, onboarding, reports, notifications_
