# Upgrade Notes

Operator / tenant-admin facing notes for behaviour changes that need attention when deploying a
new release of the HRM platform. Read this before/after a deploy that crosses one of the dated
entries below.

## How to use

- Entries are **dated, newest first**, and tagged with the decision / issue ID that drove them.
- Each entry answers three things: **What changed**, **What the platform does automatically** on
  startup (migrations / data backfills), and **Action for admins** — the manual step (if any) an
  operator or tenant admin must take after the deploy.
- "Automatic" backfills run in `DbInitializer` on application startup and are idempotent — safe to
  re-run on every boot; no manual trigger needed.
- If an entry has no **Action for admins**, there is nothing to do — it is informational.

---

## 2026-07-19 — ISSUE-285a / #390: dashboard upcoming-birthdays now index-backed (`Employee.BirthMonthDay`)

**What changed.** The dashboard "upcoming birthdays" widget previously loaded every active/probation
employee into memory and scanned month/day (the `DateOfBirth` window doesn't translate to SQL). It now
filters in SQL against a new indexed `employees.birth_month_day` column (`month*100 + day`), maintained
automatically on every write by a `SaveChanges` interceptor.

**What the platform does automatically.** Migration `20260719162127_AddEmployeeBirthMonthDayIndex` adds
the column + a `(tenant_id, birth_month_day)` index AND **backfills every existing employee row** on
startup (`UPDATE employees SET birth_month_day = EXTRACT(MONTH FROM date_of_birth)*100 + EXTRACT(DAY FROM
date_of_birth)` where DOB is set). Idempotent; the interceptor keeps it correct on all subsequent
create/update/import.

**Action for admins.** None — informational. The backfill is automatic and needs no manual step.

---

## 2026-07-13 — DEC-1 / ISSUE-291: report row-scope now uses explicit `Reports.View.Team` / `Reports.View.All`

**What changed.** The cross-module reports (HR / leave / attendance report surfaces) decide which
employees' rows a caller may see — their **row scope**. Previously that scope was inferred
indirectly: the org-wide ("All") bucket **borrowed** whatever `Employee.View.All` /
`Leave.View.All` / `Attendance.View.All` the caller happened to hold, and the "own team" bucket was
**auto-derived** purely from the caller having a direct report. As of DEC-1, report row-scope
**requires two dedicated, explicit permissions** instead:

- `Reports.View.All` — see **all** employees' report rows (org-wide).
- `Reports.View.Team` — see only the caller's **own team** (direct reports + self).

A report user who holds neither now sees **only their own rows**, regardless of what other
`*.View.All` / `*.View.Team` permissions they hold or how many direct reports they have.

**What the platform does automatically (on startup).**

- **Built-in roles** are reconciled every startup: **Manager** gets `Reports.View.Team`; **HR
  Officer**, **HR Manager**, **Tenant Admin**, **Tenant Owner**, and **Auditor** get
  `Reports.View.All`. No action needed for tenants that only use built-in roles.
- **Custom (tenant-defined) roles** are backfilled **once** (this release, ISSUE-291): any custom
  role that already held `Reports.View` (the report-endpoint gate) **and** a scope signal is given
  the matching new permission, behaviour-preservingly —
  - holds any `*.View.All` (Employee/Leave/Attendance) → gets `Reports.View.All`;
  - else holds any `*.View.Team` → gets `Reports.View.Team`.
  - "All" wins over "Team" (a role qualifying for org-wide scope gets `Reports.View.All`, not
    `Reports.View.Team`). The backfill is idempotent and never removes anything.

**Action for admins.** A custom role can only be inferred if it held a `*.View.All` / `*.View.Team`
signal at upgrade time. If you have a **custom role that *should* see team or org-wide report data
but did *not* hold such a signal** (so the automatic backfill could not infer its intended scope),
you must grant it explicitly:

1. Go to **Admin → Roles**.
2. Open the custom role.
3. Grant **`Reports.View.Team`** (own team) or **`Reports.View.All`** (org-wide) as appropriate.
4. Save.

Until you do, members of that role see **only their own rows** on report surfaces. Both permissions
are now assignable in **Admin → Roles** (added to the permission catalog by the DEC-1 front-end
change).
