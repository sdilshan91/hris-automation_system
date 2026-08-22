---
name: reference-payroll-proration-shift-aware
description: Shift-aware join/separation pro-ration (ISSUE-156/157/180) — non-obvious double-pro-ration invariants
metadata:
  type: project
---

Payroll pro-ration is SHIFT-aware (not calendar), reusing `ShiftScheduleResolver` (see [[reference-shift-schedule-resolver]]) — ISSUE-156 joiner, ISSUE-157 leaver, ISSUE-180 encashment.

**Why:** the run engine's whole-period `AttendancePayrollService.TotalWorkingDays` is shift-aware-but-NOT-holiday-aware; pro-ration must match it (ISSUE-180 wants encashment denominator == run's working-days). Holiday-awareness for the whole engine is a SEPARATE, filed out-of-lane change — do NOT make pro-ration holiday-aware (would make it inconsistent with the run).

**How to apply / durable invariants that make "no double pro-ration" work:**
- `PayrollRunProcessor.ProRataPaidDays` now counts `ShiftScheduleResolver.CountWorkingDays(set, start, end)` over `[max(monthStart,DOJ) .. min(monthEnd, separationDate)]`, resolved as-of monthStart — the SAME basis as attendance's TotalWorkingDays. `paidDaysBeforeLop = ProRataPaidDays ?? workingDays`; factor = paid/working.
- Joiner: `AttendancePayrollService` does NOT start-bound joiners (effectiveEnd only; effectiveStart is always monthStart) → TotalWorkingDays is FULL-month shift days → the single DOJ bound is applied once in ProRataPaidDays. factor = employed/full < 1.
- Leaver: the run's employee query is Active/Probation ONLY (Terminated excluded), so the leaver in the run is Active-status and attendance does NOT cut it off. Separation date comes from `EmploymentHistory` (status_change→Terminated, Max EffectiveDate) — NOT `Employee.Status`, and NOT a field on Employee. `SeparationDatesAsync` mirrors `AttendancePayrollService.TerminatedLastWorkingDaysAsync` so, if attendance HAD end-bounded the row, the two counts are EQUAL → factor 1.0 (no second pro-ration).
- Encashment: `LeaveEncashmentService.WorkingDaysInMonthAsync` (was calendar `WorkingDaysInMonth`) uses the resolver; empty shift set → counts all calendar days (unchanged fallback); guards div-by-zero.

**Test gotcha (InMemory harness):** an employee only gets an attendance row if `employeesWithRecords` includes them — that checks AttendanceLog/approved-leave/regularization/overtime, NOT the materialized monthly summary. To test the "joiner WITH attendance row" (single-pro-ration) case you must seed a real `AttendanceLog` in the period. No Tenant row seeded → tenant TZ falls back to UTC, so ClockIn compares in UTC. Golden month: Sept 2025 (Sep 1 = Monday) → 22 Mon-Fri days; DOJ Sep 18→30 = 9; term Sep 1..10 = 8; encashment 22000/22 = 1000/day (calendar 30 → 733.33).
