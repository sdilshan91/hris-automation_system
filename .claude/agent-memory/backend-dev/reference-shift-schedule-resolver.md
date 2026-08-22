---
name: reference-shift-schedule-resolver
description: BUG-125 shared batched shift/working-days resolver reused by attendance payroll + dashboard
metadata:
  type: reference
---

`ShiftScheduleResolver` (internal static, `HRM.Infrastructure/Services/ShiftScheduleResolver.cs`) is the
ONE batched shift-schedule resolution both attendance N+1-prone paths share (BUG-125). Any future
attendance working-days feature should reuse it, not re-implement per-employee shift lookup.

- `ResolveWorkingDaySetsAsync(db, employeeIds, asOf, ct)` → `Dictionary<Guid, HashSet<int>>` (ISO 1=Mon..7=Sun
  working-weekday set per employee). Exactly 3 queries regardless of employee count: default shift, effective
  assignments (`empIds.Contains` + effective-date predicate, latest `EffectiveFrom` picked in memory),
  referenced shifts. Fallback chain matches the old per-employee logic: assigned shift → default shift → empty
  set (empty = "all calendar days are working days").
- `CountWorkingDays(set, start, end)` iterates the range in memory (returns 0 when end<start).

Callers: `AttendancePayrollService.GetPayrollDataAsync` (resolve as-of monthStart, count per-employee to its
own effectiveEnd) and `AttendanceDashboardService.ComputeCustomRowsAsync` (resolve as-of `from`, count [from,to]).
The former per-employee `WorkingDaysAsync` / `ScheduledWorkingDaysAsync`+`ResolveWorkingDaysAsync` were deleted.

Indexes already cover the batch (`ix_employee_shift_employee_effective`, partial default-shift index, Shift PK)
— no migration. Correctness tests (values UNCHANGED) live in `AttendancePayrollIntegrationTests` +
`AttendanceDashboardIntegrationTests` (`*_BUG125`), asserting assigned-shift vs default-fallback per employee.
No constant-query-count test: no query-counter util exists and these suites run on InMemory (no relational
DbCommand events) per [[feedback-integration-tests-inmemory]].
