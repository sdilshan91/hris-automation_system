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

## 2026-07-20 — ISSUE-173 FR-6: payroll approval delegation (config-driven, on approver leave)

**What changed.** A payroll approval step can name a **primary approver user** and a **delegate user**. When the
run activates that step (at submit or on advancing to it) and the primary approver is on an **approved leave
spanning that day**, the run is delegated to the delegate: it records a `Delegated` approval-history row and
notifies the delegate. Delegation is a NOTIFY/record overlay — it does **not** change the role-gated approval
authorization (any holder of the step role can still approve).

**What the platform does automatically.** Migration `Payroll_ApprovalDelegation` adds three **nullable** columns
(`payroll_approval_step_config.primary_approver_user_id` + `.delegate_user_id`, `payroll_run.delegated_to_user_id`)
— all default null, so **nothing delegates until you configure a primary + delegate** on a step. The leave check
uses the same approved-leave-spanning-today logic as the rest of the app.

**Action for admins.** Optional. To enable delegation for a step, set both a **primary approver** and a
**delegate** (both must be active users holding `Payroll.Approve`) on that approval step via
`PUT /api/v1/payroll/approval/step-config`. Setting only one → 400. Leave both unset for today's behaviour.

---

## 2026-07-20 — ISSUE-173 FR-3: payroll approval SLA auto-escalation (opt-in)

**What changed.** Payroll approval steps can now carry an **SLA** and a **backup approver role**. When a run
sits in `AwaitingApproval` past its step SLA, a recurring job escalates it: it stamps the run `escalated_at`,
writes an `Escalated` approval-history row, and **notifies the backup role's holders** (falling back to the
payroll approver pool). Escalation is a NOTIFY — the run is not reassigned; any holder of the step/backup role
can still act via the pending-approvals queue.

**What the platform does automatically.** Migration `Payroll_SlaEscalation` adds four **nullable** columns
(`payroll_approval_step_config.sla_hours` + `.backup_role_id`, `payroll_run.sla_due_at` + `.escalated_at`) —
all default null, so **nothing escalates until you configure an SLA** (opt-in). A recurring Hangfire job
`payroll-approval-sla-escalation` runs every 5 minutes (per-tenant). `sla_due_at` is stamped at submit and each
step advance from the current step's `sla_hours`; reject / return-to-HR clear it.

**Action for admins.** Optional. To enable escalation for a step, set **SLA hours** (>0) and a **backup approver
role** (a role holding `Payroll.Approve`) on that approval step via `PUT /api/v1/payroll/approval/step-config`.
Leave `sla_hours` unset to keep today's behaviour (no escalation).

---

## 2026-07-20 — DF-5: notification-template language-variant cap is now plan-configurable

**What changed.** The per-(tenant, event) email-template **language-variant cap** — previously a hardcoded `2` — is now
resolved through the plan-limit system (key `max_template_language_variants`), so it can be set per plan tier or
per tenant. Precedence: a per-tenant `PlanLimitOverride` > the tenant's `SubscriptionPlan` value > a per-tenant
snapshot value > the historical default of **2**.

**What the platform does automatically.** Migration `Plan_MaxTemplateLanguageVariants` adds two nullable
`integer` columns (`subscription_plans.max_template_language_variants`, `tenants.max_template_language_variants`).
`DbInitializer` seeds `2` on newly-seeded plans. Existing plan/tenant rows stay `NULL` and the service falls back
to `2`, so **behaviour is unchanged** until you configure a value.

**Action for admins.** None required. To raise/lower the cap for a plan, set `max_template_language_variants` on
the `SubscriptionPlan` (or add a per-tenant `PlanLimitOverride` for that key). Note: an *unlimited* (null-valued)
override for this key is **not** honored as unlimited — it falls back to the plan/default (unbounded language
variants is intentionally disallowed).

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
