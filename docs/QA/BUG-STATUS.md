# Bug Status Tracker

> Single source of truth for defects found during the 2026-06-19 QA execution baseline.
> Companion to [BUG-REPORT-2026-06-19.md](reports-archive/BUG-REPORT-2026-06-19.md) (detail + repro + traces) and
> [EXECUTION-LOG-2026-06-19/](./EXECUTION-LOG-2026-06-19/) (evidence). Hand-edit the **Status** column as
> bugs are picked up / fixed / verified. Do NOT fix yet — analysis & triage phase.
>
> **Status:** `OPEN` not started · `WIP` fix in progress · `FIXED` patched, awaiting re-test ·
> `VERIFIED` re-tested green · `WONTFIX` deliberate (note why) · `DUP` duplicate.
>
> **Severity:** `CRIT` blocks core use · `HIGH` breaks a primary flow · `MED` partial/contained ·
> `LOW` cosmetic/defense-in-depth.
>
> **Layer:** `FE` frontend · `BE` backend · `TEST` test/process · `DATA` seed/config.

## Summary
| Severity | Open | Fixed | Verified | Total |
|---|---|---|---|---|
| CRIT | 0 | 0 | 2 | 2 |
| HIGH | 1 | 0 | 4 | 5 |
| MED  | 0 | 2 | 0 | 2 |
| LOW  | 1 | 0 | 0 | 1 |
| **Tracked defects** | **2** | **2** | **6** | **10** |
| Contract nits (CT) | 4 | — | — | 4 |
| TC/permission drift (TD) | 5 | — | — | 5 |
| Coverage gaps (GAP) | 2 | — | — | 2 |

> **Fix session 2026-06-19:** 6 VERIFIED (re-tested green), 1 FIXED (reasoned), 3 still OPEN.
> BUG-9 + BUG-10 were newly DISCOVERED by fixing BUG-1 (the System Admin Console became reachable,
> exposing the next-layer FE↔BE URL/shape divergences).

---

## Tracked defects

| ID | Status | Sev | Layer | Title | Feature / Module | User story | Component (file:line) | Root cause |
|----|--------|-----|-------|-------|------------------|-----------|-----------------------|------------|
| BUG-1 | ✅VERIFIED | CRIT | FE | System Admin Console unreachable — role-string mismatch | Admin Console (System) | US-ADM-001/002/009, US-AUTH-006 | **Fixed** `app.routes.ts:127,139,152` + `main-layout.component.ts` `'System Admin'`→`'SystemAdmin'` | UI re-test: `/admin/tenants` now loads (was `/forbidden`) |
| BUG-2 | ✅VERIFIED | HIGH | FE | System Admin Console missing from sidebar | Admin Console (System) | US-ADM-001/002 | **Fixed** `main-layout.component.ts` — added Tenants + Monitoring navItems | UI re-test: sidebar shows Tenants/Monitoring/Plans |
| BUG-3 | ✅VERIFIED | CRIT | FE | Platform admin served tenant-HR sidebar; every feature route 403s | Auth / RBAC / Layout | US-AUTH-006 | **Fixed** `main-layout.component.ts` `visibleNavItems()` persona filter | UI re-test: platform admin sees ONLY system items, not the tenant-HR menu |
| BUG-4 | FIXED | MED | FE | Nav items gate on permission strings absent from catalog | Cross-module nav (Leave/Attendance/Performance/Admin) | US-LV/ATT/PRF/ADM | **Fixed** `main-layout.component.ts` → real catalog perms (`Leave.View.Own`, `Tenant.ManageUsers`, `Audit.View`, …) | Reasoned (can't UI-test tenant personas in dockerized FE); awaiting tenant-persona re-test |
| BUG-5 | OPEN | HIGH | TEST | Unit tests mock the role; no E2E layer | Testing / process | — | `auth.service.spec.ts` specs aligned to `'SystemAdmin'`; **no `e2e/` project yet** | Spec strings fixed, but the core gap (no cross-layer E2E) remains OPEN |
| BUG-6 | VERIFIED | HIGH | BE | Employee dashboard `GET /dashboard/widgets` → 500 | Reports & Analytics / Dashboard | US-RPT-005 | **Fixed** `LeaveEntitlementService.cs:543` (UTC-kind `referenceDate` for the `EffectiveFrom` timestamptz query) | was `new DateTime(leaveYear,1,1)` Kind=Unspecified → Npgsql `timestamptz`. **Re-tested 500→200** (rebuilt container, employee@acme) |
| BUG-7 | VERIFIED | HIGH | BE | Attendance monthly endpoints → 500 (×3) | Attendance / Payroll prep | US-ATT-*, US-PAY-010 | **Fixed** `AttendanceSummaryService.cs:351,352,438,439` (added `DateTimeKind.Utc` to `.ToDateTime` for the `ClockIn` timestamptz query) | same root cause. **Re-tested 500→200** on `summary/monthly`, `reconciliation`, `payroll-data` |
| BUG-8 | OPEN | LOW | BE | JWT tenant claim not cross-checked vs resolved subdomain | Auth / Multi-tenancy | US-AUTH-007 | `TenantResolutionMiddleware.cs` / auth pipeline | `acme` token presented under `admin` subdomain returns 200 (no data leak observed) instead of 401/403; token `tenant_id` not validated against resolved tenant |
| BUG-9 | ✅VERIFIED | HIGH | FE | System-admin FE services call non-existent `/api/admin/*` | Admin Console (Tenants + Monitoring) | US-ADM-001/002 | **Fixed** `tenant-provisioning.service.ts` + `platform-monitoring.service.ts` → `/api/v1/system/...` (+ paths `subdomain-availability`, `tenants/plans`, `tenant-usage`) | Two services stripped `/v1` to a non-existent `/api/admin` root → 404. Hidden until BUG-1 made the page reachable + unit mocks. **UI re-test: tenant list loads (200) with real rows; Create Tenant form renders** |
| BUG-10 | FIXED | MED | FE | Monitoring dashboard crashes — whole FE↔BE contract diverged | Admin Console (Monitoring) | US-ADM-002 | **Fixed** monitoring `models` + `service` + dashboard/detail components + 4 spec files → match BE contract field-for-field | The ENTIRE US-ADM-002 FE data-layer was built to a different contract (wrapper object, `gauges[]`, PascalCase enums, `jobQueue`/`overallStatus`/`tenantsByStatus[]`). Realigned FE→BE; `ng build` 0 errors; deployed (new fields confirmed in bundle). **UI visual re-test pending** (browser tool disconnected) — API 200 + contract now matches live JSON |

> **BUG-6 + BUG-7 share one root cause** — a constructed `DateTime` reaches Npgsql `timestamptz` without
> `Kind=Utc`. Treat as one fix class; **sweep** `DateOnly.ToDateTime(` / `new DateTime(` feeding EF queries
> for other affected date-range endpoints before closing.

---

## Contract / behavioral nits (CT) — code-side, low severity

| ID | Status | Sev | Layer | Title | Component | Note |
|----|--------|-----|-------|-------|-----------|------|
| CT-1 | OPEN | LOW | BE | Inconsistent empty-state | Performance dashboard | `dashboard/overview` 404 `no_cycle` vs `dashboard/trend` 200 empty |
| CT-2 | OPEN | LOW | BE | Wrong status for precondition | Leave/Payroll/Performance self-service | `403 no_employee_record` should be `409`/`422` (not an authZ denial) |
| CT-3 | OPEN | LOW | BE | Unknown-id returns 200 `[]` not 404 | Recruitment applicant offers | cosmetic; no leak |
| CT-4 | OPEN | LOW | BE | ~3 differing list-envelope shapes | cross-module | inconsistent shapes under `ApiResponse<T>` (relates to FE↔BE envelope debt) |

## Test-case / permission drift (TD) — TC-side fixes, not code bugs

| ID | Status | Title | Note |
|----|--------|-------|------|
| TD-1 | OPEN | HR Officer has no payroll permissions | Payroll TCs assume HR Officer `Payroll.*.All`; only Tenant Admin holds them → TCs not executable as written |
| TD-2 | OPEN | `Leave.ManageLop` maps to no seeded role | Entire LOP controller unreachable by any seeded persona — confirm intended |
| TD-3 | OPEN | Notification template perm naming | Real gate is `Tenant.*Settings`, not TC's `Notifications.ManageTemplates` |
| TD-4 | OPEN | Recruitment perm naming | TCs use `Recruitment.Create.All`/`Read.All`; code uses `Recruitment.View`/`Manage` |
| TD-5 | OPEN | Stale leave permission names | TCs cite `Leave.Configure` vs code `LeaveType.Create`/`Leave.ConfigurePolicy` |

## Coverage gaps (GAP)

| ID | Status | Title | Note |
|----|--------|-------|------|
| GAP-1 | OPEN | No paged offboarding-list endpoint | `/offboarding` is lookup-by-query only; possible US-ONB coverage gap |
| GAP-2 | OPEN | No distinct attendance `View.Own`/`View.Team` endpoint | "view own" served via `/status` + `/regularizations`; TCs asserting a history endpoint untestable |

---

## Disposition of remaining items (after fix session 2)

Not every remaining item is a "just patch it" bug. Honest call on each:

| Item | Disposition | Why |
|---|---|---|
| **BUG-5** (no E2E) | **DEFER — separate build** | The real fix is standing up the E2E harness ([TEST-AUTOMATION-PLAN](plans/TEST-AUTOMATION-PLAN-2026-06-19.md)) — a multi-day effort, not a code patch. Spec role-strings already aligned. |
| **BUG-8** (token tenant not cross-checked) | **DEFER — dedicated security task** | Security-sensitive. Read isolation already scopes by resolved-tenant (`ITenantContext`), so no leak was observed. A correct guard must run post-auth and exempt `/auth/*` (login/switch-tenant/refresh), impersonation, and system context — and needs cross-flow tests I can't safely exercise in a batch. Rushing it risks breaking verified auth/admin flows. **Re-assess severity** (token acceptance across subdomains). |
| **CT-2** (403 vs 409 for "no employee record") | **WON'T FIX (deliberate)** | Thrown consistently at ~15 sites with passing tests asserting 403; 403 is defensible. Changing to 409/422 = churn ~20 files for a debatable semantic. |
| **CT-1, CT-3, CT-4** | **DEFER (cosmetic)** | Empty-state/unknown-id status inconsistencies + envelope-shape variety. Low value vs churn; groom opportunistically. |
| **TD-1, TD-2** (HR Officer no payroll perms; `Leave.ManageLop` unassigned) | **NEEDS PRODUCT DECISION** | These may be intentional (only Tenant Admin runs payroll) or real seeding gaps. *Which* role should hold the permission is a product call, not a code guess. |
| **TD-3, TD-4, TD-5** | **DEFER (TC-doc grooming)** | The designed TCs cite stale permission names; fix is editing TC markdown to the real catalog, not code. |
| **GAP-1, GAP-2** | **DEFER (feature work)** | No paged offboarding-list / no distinct attendance `View.Own` endpoint = building new endpoints, not fixing bugs. |

## Notes
- **QA scope covered:** prioritized critical + smoke, all 11 modules, ~262 API+UI checks (NOT the full
  1,941 designed TCs — see [QA-COVERAGE-REPORT-2026-06-19.md](reports-archive/QA-COVERAGE-REPORT-2026-06-19.md)).
- **Verdict:** backend ~245/262 PASS, 0 isolation breaches; failures = FE auth-wiring (BUG-1/2/3) + the
  date-kind 500 class (BUG-6/7). All Status=OPEN; fixing deferred to the next phase.
- **Suggested fix order:** BUG-6/7 (one shared date-kind fix, sweep first) → BUG-1 (one-word) → BUG-3/2/4
  (FE persona/nav) → BUG-8 → CT/TD/GAP grooming. Re-run the baseline after each for the "after" snapshot.
