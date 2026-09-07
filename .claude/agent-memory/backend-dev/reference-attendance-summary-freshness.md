---
name: reference-attendance-summary-freshness
description: ISSUE-083 — attendance monthly summary recomputes the CURRENT month on read, serves CLOSED months from the materialized row; plus the InMemory harness clock gotcha
metadata:
  type: reference
---

`AttendanceSummaryService.GetMonthlyAsync` has two freshness regimes, and they are easy to get
wrong in opposite directions:

- **CURRENT month** (judged by `TenantClock.TodayIn(tenantZone)`, the same seam `MonthBounds` uses —
  never `DateTime.UtcNow` directly) → calls `GenerateAsync` on every read. This is what keeps the
  summary row and `GetEmployeeBreakdownAsync` (which has *always* computed live, per-day
  `ComputeDay`) from telling different stories. A read on the current month therefore **writes**.
- **CLOSED month** → served verbatim from `attendance_monthly_summary`. Do not "fix" this into a
  blanket recompute; the cheap closed-month read is a deliberate performance property.

Consequence worth remembering: `attendance_monthly_summary` has a UNIQUE index
(`tenant_id, employee_id, year_month` filtered on `is_deleted = false`), so the insert race is
confined to the first-ever read of a month; every later current-month read takes the update branch
and is last-writer-wins with identical values. Recomputing on read did **not** widen that window.

**Harness gotcha (`MonthlySummaryIntegrationTests`):** the seeded tenants have **no `Tenants` row**,
so `ResolveTenantZoneAsync` finds no `TimeZone` and `TenantClock.ResolveTimeZone` falls back to UTC.
That is why a test can legitimately derive "current month" from `DateTime.UtcNow` there — it IS the
tenant-local basis in that fixture. Seed a `Tenant` with a real zone and that stops being true.

Related: [[reference-attendance-module]], [[feedback-integration-tests-inmemory]].
