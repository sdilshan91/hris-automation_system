# Bug Report — Platform Admin Routing / RBAC Failures (2026-06-19)

**Author:** @qa-engineer (analyze-and-report only)
**Investigation source:** Live Playwright session — login `admin@hrm.local`, role `SystemAdmin`, tenant subdomain `platform`.
**Symptom:** The platform admin can reach ONLY `/dashboard`; every other route redirects to `/forbidden`. The platform admin **cannot add tenants**.
**Scope of this document:** Formal write-up of 5 confirmed bugs, validated against the existing IEEE-829 test cases under `docs/QA/`. **No TCs, source, or PRs were modified.** All cited code paths were re-verified against the working tree during this analysis.

---

## BUG-1 — System Admin role-string mismatch locks the System Admin Console

| Field | Value |
|-------|-------|
| **ID** | BUG-2026-0619-1 |
| **Severity** | **CRITICAL** |
| **Component / persona** | Frontend route guards / platform `SystemAdmin` |
| **Affected routes** | `/admin/tenants`, `/admin/plans`, `/admin/monitoring` (System Admin Console) |
| **Confidence** | **99%** — confirmed in code AND live (`hasRole('SystemAdmin')`=true, `hasRole('System Admin')`=false) |

**Steps to reproduce**
1. Log in as `admin@hrm.local` (seeded role `SystemAdmin`) at the `platform`/`admin` context.
2. Navigate by direct URL to `/admin/tenants` (or `/admin/plans`, `/admin/monitoring`).
3. Observe redirect to `/forbidden`.

**Expected vs Actual**
- **Expected:** The System Admin reaches the tenant-provisioning, plans, and monitoring consoles.
- **Actual:** All three redirect to `/forbidden`. Tenant provisioning is unreachable.

**Root cause**
The backend seeds the platform role as `SystemAdmin` (no space):
- `src/backend/HRM.Infrastructure/Persistence/DbInitializer.cs:16` → `private const string SystemAdminRoleName = "SystemAdmin";` (assigned to the admin user at `:174`).

The System-Admin-Console route guards check `'System Admin'` (WITH a space):
- `src/frontend/src/app/app.routes.ts:127` → `roleGuard(['System Admin'])` on `/admin/tenants`
- `src/frontend/src/app/app.routes.ts:139` → `roleGuard(['System Admin', 'System Support'])` on `/admin/monitoring`
- `src/frontend/src/app/app.routes.ts:152` → `roleGuard(['System Admin'])` on `/admin/plans`

`hasRole` is an exact `.includes` match — `src/frontend/src/app/core/auth/auth.service.ts:420-422`:
```ts
hasRole(role: string): boolean {
  const claims = this.decodeToken();
  return claims?.roles?.includes(role) ?? false;
}
```
`'SystemAdmin'.includes` never matches the guard's `'System Admin'`, so every guard denies and redirects to `/forbidden`. (The "Plans" nav `role: 'System Admin'` at `main-layout.component.ts:814` is broken by the same mismatch.)

**Suggested fix**
Align the strings to a single canonical token. Cheapest, lowest-blast-radius change: edit the three guards (and the Plans nav `role`) from `'System Admin'` → `'SystemAdmin'`. Alternatively rename the seeded role — higher risk (touches JWT claims, seed data, reconciliation at `DbInitializer.cs:381`). **Recommend changing the FE strings to `'SystemAdmin'`** to match the already-issued tokens. Also add a shared role constant to prevent re-divergence.

---

## BUG-2 — System Admin Console has no sidebar navigation entry

| Field | Value |
|-------|-------|
| **ID** | BUG-2026-0619-2 |
| **Severity** | **HIGH** |
| **Component / persona** | Frontend sidebar (`main-layout`) / platform `SystemAdmin` |
| **Affected routes** | `/admin/tenants`, `/admin/monitoring` (no nav entry at all); `/admin/plans` (entry exists but broken by BUG-1) |
| **Confidence** | **97%** |

**Steps to reproduce**
1. Log in as the seeded `SystemAdmin`.
2. Inspect the sidebar.
3. Observe there is no "Tenants" or "Monitoring" item; the only platform item is "Plans".

**Expected vs Actual**
- **Expected:** The platform admin sees nav entries to reach Tenants, Monitoring, and Plans.
- **Actual:** `navItems` has **no** entry for `/admin/tenants` or `/admin/monitoring`. The fully-built tenant-provisioning feature (`src/frontend/src/app/features/admin/tenants/` — `tenant-list`, `tenant-create`) is unreachable from the UI even if BUG-1 were fixed.

**Root cause**
The `navItems` array (`src/frontend/src/app/layouts/main-layout/main-layout.component.ts:667-817`) ends at the "Plans" entry (`:810-816`) and contains no `/admin/tenants` or `/admin/monitoring` entries.

**Suggested fix**
Add nav entries for Tenants and Monitoring, role-gated to the canonical SystemAdmin token (coordinate string with BUG-1's fix). Ideally split the sidebar so the platform persona gets a platform menu rather than the tenant-HR menu (see BUG-3).

---

## BUG-3 — Platform admin is served the tenant-HR sidebar; every feature route 403s

| Field | Value |
|-------|-------|
| **ID** | BUG-2026-0619-3 |
| **Severity** | **CRITICAL** |
| **Component / persona** | Frontend sidebar + route guards / platform `SystemAdmin` |
| **Affected routes** | `/departments`, `/job-titles`, `/employees`, `/payroll`, `/recruitment`, `/reports`, `/onboarding`, `/my-payslips`, `/leave` — all → `/forbidden`. Only `/dashboard` works. |
| **Confidence** | **95%** |

**Steps to reproduce**
1. Log in as the seeded `SystemAdmin`.
2. Click any tenant-HR sidebar item (e.g. Departments, Employees, Payroll).
3. Observe redirect to `/forbidden`.

**Expected vs Actual**
- **Expected:** A platform admin is not shown tenant-HR features (it operates the platform, not a tenant's HR data); the persona has a coherent reachable menu.
- **Actual:** The sidebar shown to the platform `SystemAdmin` is the **tenant-HR menu**. Nav items gate on **permissions** (SystemAdmin holds AllPermissions, so several render), but the feature **route guards** gate on **tenant roles** (`roleGuard(['Tenant Admin','HR Officer'])`, `['Employee','Manager',…]`) that a platform SystemAdmin does not hold. The menu invites clicks that all 403.

**Root cause — the permission-gate vs role-gate split**
- Nav items gate on permission strings, e.g. `main-layout.component.ts` Employees/Payroll/etc. (and the catalog grants SystemAdmin AllPermissions, so they appear).
- Feature routes gate on tenant **roles**, e.g. `app.routes.ts:319/330/341` (`roleGuard(['Tenant Admin','HR Officer'])`), `:399` (recruitment role list), `:374/385` (`['Employee','Manager','HR Officer','Tenant Admin']`). None of these lists includes a platform/SystemAdmin role, so the guard denies → `/forbidden`.

**Suggested fix**
Serve a persona-appropriate sidebar: when the principal is the platform `SystemAdmin`, render the platform menu (Tenants/Monitoring/Plans) and suppress tenant-HR items. Treat the nav permission-gate and the route role-gate as a single contract so a rendered item is always reachable. (This is the structural root; BUG-1/2/4 are facets of the same gate-divergence.)

---

## BUG-4 — Nav items gate on permission strings absent from the catalog (latent, affects ALL users)

| Field | Value |
|-------|-------|
| **ID** | BUG-2026-0619-4 |
| **Severity** | **MEDIUM** |
| **Component / persona** | Frontend sidebar `navItems` / all users |
| **Affected items** | Leave, Attendance, Performance, Users, Roles, Settings, Workflows, Audit Log, Data Export |
| **Confidence** | **92%** |

**Steps to reproduce**
1. Log in as any user (including a Tenant Admin with AllPermissions).
2. Inspect the sidebar for Leave / Attendance / Performance / Users / Roles / Settings / Workflows / Audit Log / Data Export.
3. Observe these items never render (their gating permission string is never present in any token).

**Expected vs Actual**
- **Expected:** Items gate on permission strings that exist in the catalog.
- **Actual:** Several nav items gate on strings that do **not** exist in the permission catalog, so they render for no one.

**Root cause**
`navItems` uses bare strings that the catalog never emits. Confirmed against `src/backend/HRM.Domain/Authorization/PermissionCatalog.cs`:
- Nav `'Leave.View'` (`main-layout.component.ts:692`) — catalog has `Leave.View.Own/.Team/.All` (`PermissionCatalog.cs:90-92`).
- Nav `'Attendance.View'` (`:698`) — catalog has `Attendance.View.Own/.Team/.All` (`:139-141`).
- Nav `'Performance.View'` (`:751`) — catalog has `Performance.View.Own/.Team/.All` (`:216-218`).
- Nav `'Admin.Users.Manage'` (`:773`) — catalog has `Tenant.ManageUsers` (`:283`).
- Nav `'Admin.Roles.Manage'` — catalog has `Roles.Manage` (`:274`).
- Nav `'Admin.View'` (Settings `:786`, Workflows `:793`, Audit Log `:800`, Data Export `:807`) — catalog has `Tenant.ViewSettings` (`:281`) etc.; no `Admin.View` exists.

**Suggested fix**
Replace each nav permission with a catalog-real string (or `hasAnyPermission([...])` over the `.Own/.Team/.All` variants). Add a build/test assertion that every nav-item permission/role string exists in the catalog/role set, so dead nav gates can't ship again.

---

## BUG-5 — Tests mock the bug away; no E2E (process root cause)

| Field | Value |
|-------|-------|
| **ID** | BUG-2026-0619-5 |
| **Severity** | **HIGH** (process) |
| **Component / persona** | Test suite (FE unit specs + IEEE-829 TCs) |
| **Affected** | Whole RBAC/routing surface |
| **Confidence** | **96%** |

**Findings (verified)**
- **296** frontend `.spec.ts` unit specs; **0** E2E tests — no `e2e/`, `cypress/`, or `playwright.config.*` under `src/frontend`.
- FE specs reference the role with the **wrong (matching-the-guard) string**: `'System Admin'` (with space) appears in 2 specs — `auth.service.spec.ts:294,307` and `tenant-monitoring-detail.component.spec.ts`; **0** specs use the real seeded `'SystemAdmin'`. The unit tests mock the role to MATCH the broken guard, so the suite stays green while production (seeded `SystemAdmin`) fails.
- All **1941** IEEE-829 markdown TCs are `status: draft` — **0 executed**. No layer exercised the real seeded role against the real guard.

**Suggested fix**
Add at least one E2E smoke test that logs in as the **actually seeded** admin and asserts the platform console is reachable (would have caught BUG-1/2/3 immediately). Change the FE specs to assert against the seeded `'SystemAdmin'` token (do **not** keep mocking the guard string). Begin executing the critical-path TCs (auth RBAC, tenant provisioning) instead of leaving them `draft`.

---

## Validation against existing test cases (Bug → Covering TC → Would it catch?)

All cited TCs were read during this analysis. Verdict legend: **(a)** no TC exists; **(b)** TC exists but is un-executed `draft`/`blocked`; **(c)** even if executed, its written steps would NOT catch the bug (wrong layer / wrong role string / mocked away).

| Bug | Covering TC(s) | Would it catch? | Gap |
|-----|----------------|-----------------|-----|
| **BUG-1** (role-string mismatch on `/admin/*` guards) | `TC-ADM-001-01` (System Admin provisions tenant, e2e) — precond correctly says `SystemAdmin` role claim, but step 1 *assumes* the form opens and tests the **provisioning API/flow**, not the **FE route guard**. `TC-ADM-009-01` (Plans list) precond `SystemAdmin`, but steps test the **Plans backend list**, not the guard. `TC-ADM-002-09` step 4 tests `Tenant Admin → /monitoring → 403` (correct deny) but **never** tests the seeded SystemAdmin being *wrongly* denied. | **No.** (b) all draft **and** (c) all test the backend/API or a deny-case; none asserts the seeded `SystemAdmin` token *passes* the FE `roleGuard(['System Admin'])`. The exact `'System Admin'` vs `'SystemAdmin'` string is never asserted at the guard layer. | No TC exercises the FE route guard with the real seeded role string. Add a guard-level / E2E TC: "seeded SystemAdmin reaches `/admin/tenants`, `/admin/plans`, `/admin/monitoring`." |
| **BUG-2** (no sidebar entry for Tenants/Monitoring) | `TC-ADM-002-09` is the closest (mentions route guard + monitoring) but tests **deny by role**, not **nav presence**. No `navItems`-rendering TC found in `docs/QA/admin-console/`. | **No.** (a) effectively no covering TC + (c) the nearest is an API/deny test. | No TC asserts the platform persona's sidebar contains reachable Tenants/Monitoring entries. Add a layout/navigation TC for the SystemAdmin persona. |
| **BUG-3** (tenant-HR sidebar served to platform admin; feature routes 403) | `TC-AUTH-016` (Tenant Admin *can* access admin endpoints), `TC-AUTH-017` (Employee *denied* 403 — incl. "UI shows a clean 403 page" step 9), `TC-AUTH-018` (roles tenant-scoped). All are **backend API 403** tests for *tenant* roles; none covers the *platform SystemAdmin* persona hitting *tenant-HR* routes, nor the permission-gate-vs-role-gate divergence. | **No.** (b) draft + (c) wrong persona/layer — they verify tenant-role denials at the API, not that a platform admin is shown an unreachable tenant-HR menu. | No TC covers the platform SystemAdmin persona's reachable route set, nor that every rendered nav item is route-reachable. Add a persona-coherence TC (nav-gate ⇔ route-gate). |
| **BUG-4** (nav permission strings absent from catalog) | None. No TC cross-checks `navItems` gating strings against `PermissionCatalog`. `TC-AUTH-016/017` reference real permissions (`User.Manage`, `Payroll.View`) but never assert nav-item visibility against the catalog. | **No.** (a) no covering TC. | Add a TC (or build assertion) that every nav permission/role string exists in the catalog/role set. |
| **BUG-5** (tests mock the bug; no E2E) | This bug *is* the test-process gap. The 1941 TCs are all `draft`; the 2 specs referencing the role use the wrong `'System Admin'` string. | **No** — by definition the current suite is green while prod fails. | No E2E layer; specs mock the broken string; no TC executed. Add E2E smoke against the seeded admin + start executing critical-path TCs. |

**Cross-cutting note:** The single highest-value missing test is an **E2E login-as-seeded-admin smoke** test. It does not exist (BUG-5) and would have caught BUG-1, BUG-2, and BUG-3 on first run. Every existing "covering" TC is either backend-API-scoped, a deny-case, or mocks the role to the guard's (wrong) string — so the suite is green by construction.

---

## Severity & priority summary

| Bug | Title | Severity | Blast radius | Covered by an executable TC that would catch it? |
|-----|-------|----------|--------------|--------------------------------------------------|
| BUG-1 | SystemAdmin role-string mismatch on `/admin/*` guards | CRITICAL | Platform admin cannot provision tenants / reach console | No |
| BUG-3 | Tenant-HR sidebar served to platform admin; routes 403 | CRITICAL | Platform admin can only reach `/dashboard` | No |
| BUG-2 | No sidebar entry for Tenants/Monitoring | HIGH | Built feature unreachable from UI | No |
| BUG-5 | Tests mock the role; no E2E | HIGH | Whole RBAC/routing surface ships unverified | No |
| BUG-4 | Nav permission strings absent from catalog | MEDIUM | Several nav items render for no one (all users) | No |

## Recommended fix order

1. **BUG-1 (first — one-word alignment).** Change the three `/admin/*` guards (and the Plans nav `role`) from `'System Admin'` → `'SystemAdmin'`, or introduce a shared role constant. Smallest diff, unblocks tenant provisioning immediately.
2. **BUG-2.** Add Tenants + Monitoring nav entries (gated on the now-aligned SystemAdmin token).
3. **BUG-3.** Serve a persona-appropriate sidebar to the platform SystemAdmin (platform menu, suppress tenant-HR items); align the nav-permission-gate with the route-role-gate so rendered items are always reachable. This is the structural root that subsumes 1/2/4.
4. **BUG-4.** Replace dead nav permission strings with catalog-real ones; add a nav-string ⇔ catalog assertion.
5. **BUG-5.** Add an E2E smoke test against the seeded admin and begin executing the critical-path RBAC/provisioning TCs; fix the 2 specs to assert the seeded `'SystemAdmin'` string instead of mocking the guard's wrong value.

---

### Reported to caller (out of @qa-engineer's lane — do NOT fix here)
All five fixes touch **`src/`** (frontend guards/nav, possibly backend seed) — outside the QA agent's write scope. This document is analysis only; the owning dev agents (`@frontend-dev`, `@backend-dev`) must apply the fixes. A follow-up QA task should add the missing guard-level/E2E/persona TCs once the gate contract is decided.

---

# Addendum — runtime BACKEND defects found by the execution baseline (2026-06-19)

BUG-1..5 above are frontend/process. The agent-driven API execution baseline (with seeded `acme`
personas + a real linked Employee record) surfaced **real, reproduced, root-caused backend 500s** that
the unit suites never hit because no real-data path was exercised. See [EXECUTION-LOG-2026-06-19/](../EXECUTION-LOG-2026-06-19/).

### BUG-6 — HIGH — Employee dashboard `GET /api/v1/dashboard/widgets` returns 500
- **Repro:** log in as `employee@acme.test` (acme), GET `/api/v1/dashboard/widgets` → **500** (Tenant Admin → 200). This is the **default landing page for the most common role** — every real employee's home screen crashes.
- **Root cause (from stack trace):** `System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` in `LeaveDashboardService.ResolveEntitlementAsync` (`HRM.Infrastructure/Services/LeaveDashboardService.cs:273`), via `GetMyBalancesAsync` → `DashboardService.LeaveBalanceWidgetAsync:550` → `BuildEmployeeWidgetsAsync:247`.
- **Fix:** UTC-kind the leave-year date bounds before the EF query (`DateTime.SpecifyKind(.., DateTimeKind.Utc)` / build from `DateOnly` as UTC). Also make the widget loop fail-soft so one widget can't 500 the whole dashboard.
- **Confidence:** 99% (reproduced + trace).

### BUG-7 — HIGH — Attendance monthly endpoints return 500 (same date-kind root cause)
- **Repro:** as Tenant Admin/HR, GET `/api/v1/attendance/summary/monthly?month=2026-06`, `/attendance/reconciliation?month=2026-06`, `/attendance/payroll-data?month=2026-06` → **all 500**. Blocks HR monthly views + the pre-payroll reconciliation/handoff.
- **Root cause:** same `DateTime.Kind=Unspecified → timestamptz` error in `AttendanceSummaryService.LoadEmployeeMonthContextAsync` (`HRM.Infrastructure/Services/AttendanceSummaryService.cs:349`); all three endpoints share `AttendanceSummaryService`.
- **Fix:** UTC-kind the month-range bounds in `LoadEmployeeMonthContextAsync`.
- **Confidence:** 99%.

> **BUG-6 + BUG-7 are one defect class** — constructed `DateTime`s passed to Npgsql `timestamptz`
> without `Kind=Utc`. Likely **more** date-range endpoints are affected wherever a `DateOnly`/month is
> converted to `DateTime` for a query. Recommend a codebase sweep for `DateOnly.ToDateTime(` / `new DateTime(`
> feeding EF queries, and a convention (always build UTC). This is the highest-value backend fix.

### BUG-8 — LOW (defense-in-depth) — JWT tenant claim not cross-checked vs resolved subdomain
- **Observed (recruitment agent):** an `acme` user's token presented under the `admin` subdomain returned 200 (empty, **no data leak**) instead of 401/403. The resolved-tenant vs token-`tenant_id` aren't cross-validated. No isolation breach observed, but it's a missing guard.
- **Fix:** reject when `token.tenant_id` ≠ resolved tenant (except deliberate system context). **Confidence:** 70%.

### Lower-severity / contract (not 500s) — for TC + backend grooming
- Empty-state inconsistency: `dashboard/overview` 404 `no_cycle` vs `dashboard/trend` 200 empty (Performance).
- `403` used for the precondition "no employee record linked" (Leave/Payroll/Performance self-service) — should be `409`/`422`.
- Unknown-id reads sometimes 200 `[]` instead of 404 (recruitment offers).
- ~3 differing list-envelope shapes under `ApiResponse<T>`.
- **Permission/TC drift** (TC-side): HR Officer has no payroll perms; `Leave.ManageLop` maps to no seeded role; template perms are `Tenant.*Settings` not `Notifications.ManageTemplates`; recruitment TCs use `Recruitment.Create.All`/`Read.All` vs real `Recruitment.View`/`Manage`. Designed TCs need reconciling to the catalog.
- No paged offboarding-list endpoint; no distinct attendance `View.Own`/`View.Team` endpoint (coverage gaps).
