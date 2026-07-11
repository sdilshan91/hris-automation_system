---
name: us-att-006-overtime-findings
description: 2026-06-26 US-ATT-006 overtime API test pass (17 owned TCs PASS, 1 BLOCKED) — engine solid; dual-standard + HR-approve gaps
metadata:
  type: project
---

# US-ATT-006 Overtime Tracking & Approval — test pass 2026-06-26

REPORT-ONLY API pass, 18 owned TCs (TC-ATT-067..083 + TC-ATT-ISO-009): **17 PASS, 1 BLOCKED (083 fe-platform-bound a11y)**. Non-owned cross-ref TCs left draft: TC-ATT-084/092/095/099 (ATT-007), 107/108/109/115/116 (ATT-008), 120/121/123 (ATT-009), 129/135 (ATT-010), TC-ATT-016 (ATT-002, already pass).

**OT engine is well-built.** Routes `/api/v1/attendance/overtime/{pre-approval,my,pending,{id}/approve,{id}/reject,report}`. Perms: pre-approval+my = `Attendance.CheckIn`; pending+approve+reject = `Attendance.Approve.Team`; report = `Attendance.View.All`. OvertimeService mirrors RegularizationApprovalService.

## Verified-correct facts (skip rediscovery)
- **Auto-flag/threshold:** clock-out auto-creates overtime_record when net > resolved-standard + `OvertimeMinimumThresholdMinutes` (default **30**, settings-driven). Gate is exact: net 449 (excess 29)=no record, net 451 (excess 31)=record OT 31. Status PENDING/AUTO_DETECTED, multiplier stamped, atomic in clock-out tx (no extra API).
- **Standard baseline = SHIFT-derived, not StandardWorkMinutes.** `ResolveStandardMinutesAsync` returns assigned-shift `(end-start)-break`. John Doe has "Day Shift" 09:00-17:00 break60 = **420 net**. So net-540 → OT **120** (not 60). The clock-out LOG overtimeMinutes uses StandardWorkMinutes(480)→60. **ISSUE-078 MED: dual standard, log vs record disagree same tx.** **ISSUE-082 LOW TEST: TC-067/068/070 expected values assume 480.**
- **Multipliers (TC-069 PASS):** weekday 1.5 / weekend(Sat) 2.0 / public-holiday 2.5, stored not applied. Holiday calendar IS wired (`holiday` table, type='Public'). Precedence holiday>weekend>weekday (resolver checks holiday first). To test weekend: backdate clock_in to a past Saturday (2026-06-20). Holiday: insert a past weekday Public holiday (e.g. 2026-06-24) then delete.
- **Daily cap (TC-070 PASS):** caps OT at `MaxDailyOvertimeMinutes`(240). excess 240=OT240 not flagged; excess 241=OT240 + `daily_cap_applied=t`. calc_basis records raw vs capped.
- **Weekly cap (TC-071 PASS, seam):** Monday-anchored week sum (approved+pending) crossing `MaxWeeklyOvertimeMinutes`(1200) sets `weekly_cap_exceeded=t` + Serilog WRN "Overtime cap exceeded ... weeklyCapExceeded=true". Does NOT cap minutes (alert only, BR-5). Dispatch DEFERRED US-NTF.
- **Pre-approval (TC-072 PASS):** `require_overtime_pre_approval` (default false). ON + no matching PRE_APPROVED for date → UNAPPROVED (not payroll-ready). ON + matching → PENDING. OFF → PENDING/AUTO_DETECTED. Submit rejects past-date(`past_date`)/short-reason(<10)/zero-hours. **Date-match gotcha:** OT date = clock_in UTC date; high-hours OT lands on "yesterday" UTC but pre-approval submit refuses past dates → to test the matching path insert a PRE_APPROVED row for the past date directly.
- **Approve/reject/adjust (TC-074/075/076 PASS):** approve→APPROVED, approved_minutes=detected (or adjusted), is_payroll_ready=t, overtime_approval_history row. Adjust DOWN preserves overtime_minutes, sets approved_minutes; over-bound (>detected) rejected `invalid_approved_minutes`; approve-zero accepted (approved 0, payroll-ready t). Reject needs reason ≥10 chars else 400; REJECTED not payroll-ready, reason in manager_comment+history.
- **Team-scope/self-approval (TC-073/077 PASS):** queue = direct reports (ReportsToEmployeeId==manager) PENDING only; out-of-team excluded; self-approve→403 `self_approval`; own record absent from own queue. Employee→403 on approve/reject/report/pending.
- **Immutability (TC-078 PASS):** decided records → 409 `already_actioned` on re-approve/reject/flip. History append-only.
- **Report (TC-079 PASS):** per-employee approved/pending/rejected + totals, month-scoped; empty month zeroed; invalid month 400 `invalid_month`.
- **Determinism (TC-080 PASS):** pure `AttendanceCalculator.CalculateOvertime`; `calculation_basis` string records all inputs; identical inputs→identical output.
- **Security (TC-082 PASS):** all endpoints 401 no-token; SQL injection inert (parameterized); body-injected employeeId/tenant_id IGNORED (stamped acme + acting employee).
- **Perf (TC-081 PASS w/ caveat):** pending queue p95=21ms (30 seq), far under 2s. Full ~50-report load profile NOT seedable (small fixture).
- **Isolation (TC-ISO-009 PASS on demonstrable arms):** body injection ignored, write clean (0 rows to platform under spoofed header), self-resolve = no leak. acme-JWT+platform-header ACCEPTED 200 (BUG-003 root) but employee-self-resolve → empty/403-no-employee, no leak. **No globex tenant seeded** → cross-tenant approve/reject arm not positively demonstrable.

## New findings (shared ledger)
- **BUG-049 MED**: HR Officer (hr@acme.test) has Attendance.Approve.Team but NO linked employee → 403 "No employee record linked" on every approve/reject; + no HR/supervisor-fallback branch → a top-level manager's (reports_to=null) own OT is un-approvable forever (BR-8 "or HR" unmet). Self-approve deny + route-to-direct-supervisor DO work.
- **ISSUE-078 MED**: dual standard (log 480 vs record shift-420) — see above.
- **ISSUE-079 LOW**: daily_cap_applied/weekly_cap_exceeded persisted+logged but NOT on any DTO (no API source for §8 cap UI / FR-8 alert).
- **ISSUE-080 LOW**: UNAPPROVED OT minutes invisible in monthly report (in recordCount, absent from the 3 minute columns).
- **ISSUE-081 LOW**: no overtime report export endpoint (§8 export button unbacked).
- **ISSUE-082 LOW TEST**: TC-067/068/070 expected minutes assume std 480; engine uses shift std 420.
- **ENH-007**: surface weekly running OT total vs cap for §8 progress bar.
- **EXTENDED not re-filed:** ISSUE-067/069/071/073/075 (attendance no central audit_logs) — clock-out OT + pre-approval write none; BUT approve/reject DO write immutable overtime_approval_history (better than clock-in/out). BUG-003 (JWT-vs-subdomain) — overtime self-resolve = no leak.

## acme residue (left in DB, REPORT-ONLY)
~24 overtime_record rows for John Doe (EMP-0001) across 2026-06-20/24/25 (PENDING/APPROVED/REJECTED/UNAPPROVED from this run), 1 for Speed Test (EMP-0002, "QA out-of-team test"), 1 for Team Manager (EMP-MGR01, "QA self-approval test" PENDING). 1 PRE_APPROVED 2026-06-26 + injected-tenant test rows for John. attendance_settings restored: require_overtime_pre_approval=false. No temp holiday left (QA-OT-HOLIDAY deleted). Zero writes to platform/techoneglobal. Backend left running.

## Method
psql at `/c/Program Files/PostgreSQL/18/bin/psql.exe`, DB hris_dev_db / developer / pw in user-secrets. Helper to gen OT: clock John in (needs `Content-Type: application/json` else 415), backdate open log clock_in to `now() - interval 'N minutes'` (keep tz; net = gross−60 if gross>360), clock-out. See [[testing-loop-report-only]] [[qa-no-debugger-for-perf]] [[us-att-002-clockout-findings]] [[qa-personas-reseed-2026-06-25]].
