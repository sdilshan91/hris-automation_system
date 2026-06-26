---
name: us-att-004-approve-reject-findings
description: 2026-06-26 US-ATT-004 manager approve/reject regularization API test pass — 11P/1F/3B; routes, exact behaviors, findings ISSUE-073/ENH-005, BUG-003 ext
metadata:
  type: project
---

# US-ATT-004 manager approve/reject regularization — 2026-06-26 API pass (11 PASS / 1 FAIL / 3 BLOCKED, 15 owned)

REPORT-ONLY run. **Why:** execute the 15 owned US-ATT-004 TCs vs running stack. **How to apply:** reuse routes/verdicts when re-testing approve/reject or attendance audit.

## Routes (AttendanceController, all `Attendance.Approve.Team`)
- `GET /api/v1/attendance/regularizations/pending` — queue (TC-040 spec'd `approval-queue` but actual is `/pending`)
- `POST /api/v1/attendance/regularizations/{id}/approve` body `{comment?}`
- `POST /api/v1/attendance/regularizations/{id}/reject` body `{reason}` (min 10 chars)
- `POST /api/v1/attendance/regularizations/bulk-approve` body `{regularizationIds:[],comment?}`
- Tables are **singular snake_case**: `attendance_regularization`, `attendance_regularization_history`, `attendance_log`, `attendance_period_lock`, `audit_logs` (plural), `employees`, `tenants`. psql at `/c/Program Files/PostgreSQL/18/bin/psql.exe`, developer/Sanjesi#123/hris_dev_db.

## Verified-EXACT behaviors (RegularizationApprovalService.cs)
- **Approve applies correction + recalcs:** MISSED_BOTH 09:00-17:30 (510 span) → attendance_log CREATED, total_work_minutes=**450** (510-60 break), status COMPLETE, OT=0. Same AttendanceCalculator as clock-out (matches us-att-002 worked-hours facts). reg.status=APPROVED, attendance_log_id linked, updated_by=manager@acme.test. History row (approver=EMP-MGR01, level 1, action APPROVED, comment, actioned_at).
- **Reject:** status REJECTED, history row w/ reason, **attendance_log UNTOUCHED** (verified linked log stayed 331/ANOMALY). Reason min 10: empty/omitted/9-char → 400; exactly 10 ("Valid one.") → 200. Service double-guards reason<10 even though validator does too.
- **Team-scope:** requester.ReportsToEmployeeId != manager.Id → 403 `not_team_member` EXACT "You are not authorized to approve requests for this employee." (both approve+reject). Non-report excluded from queue.
- **Self (BR-6):** reg.EmployeeId==manager.Id checked BEFORE team check → 403 `self_approval` "You cannot approve your own regularization request. It must be approved by your manager or HR." Self reg not in own queue. (Routing-to-supervisor DEFERRED — no workflow engine; MGR01 has null reports_to.)
- **Already-actioned (BR-3):** non-Pending → 409 `already_actioned` "...(status: APPROVED/REJECTED)." 1 history row + 1 log each, no dup side effects. NFR-4 immutability de-facto (no update/delete route).
- **Bulk (BR-7):** per-item independent; eligible all approved (each own log 450/COMPLETE, own history w/ bulk comment); non-team→not_team_member, decided→already_actioned reported per-item, no batch corruption. Idempotent (re-run = all already_actioned).
- **Payroll lock (BR-5):** AttendancePeriodLock table EXISTS + checked at approve time → 409 `payroll_period_locked` "This date falls within a locked payroll period. Please contact HR." reg stays PENDING. (TC-045 testable LIVE, not just deferred — seed an active lock row.)
- **Perf:** k6 200 iters/5VU on `/pending` p95=22ms (budget 2000). 50-row volume NOT seeded (only ~2-7 pending) — PASS-with-caveat.

## Findings (shared ledger)
- **ISSUE-073 MED BE** — approve/reject/bulk write **ZERO `audit_logs`** rows (count=0 for regularization). Decisions go to `attendance_regularization_history` + Serilog INF only. FR-6/NFR-4/AC-1-step6 unmet → TC-048 FAIL. Same gap family as ISSUE-067(clock-in)/069(clock-out)/071(submit). Denied authz attempts (TC-041/042) also unaudited.
- **ENH-005** — FR-5 notify + FR-8 Redis cache are no-op seams (DEFERRED US-NTF/no-Redis); AC-4 multi-level (US-ADM-007 engine) absent → single-level final approve mutates log immediately, workflow_instance_id null.
- **BUG-003 EXTENDED (not re-filed):** acme JWT + `X-Tenant-Subdomain: techoneglobal` on `/pending` → 200 **empty** queue (not rejection) = TenantResolutionMiddleware no token-vs-header guard. BUT write/decision arm fails CLOSED: foreign-header approve of acme reg → 403 "No employee record"/404, acme reg stayed PENDING, **0 rows in techoneglobal**. No leak (empty not other-tenant rows), no cross-tenant write. ISO-007 PASS materially.

## acme RESIDUE (cleanup TODO)
John Doe (019efced) reg statuses now: 2026-06-19/20/22/23 **APPROVED** (created logs), 2026-06-25 **REJECTED**, 2026-06-22 & older REJECTED (prior), 2026-06-24 & 2026-06-26 still PENDING. **2 seed rows inserted** (left PENDING): `019f0200-1111-...04a1` (Du1 Test, non-report, TC-041) + `019f0200-2222-...04b2` (MGR01 self, TC-042). Test period-lock seed `019f0200-3333-...04c3` was DELETED (restored). No writes to techoneglobal. See [[us-att-002-clockout-findings]] [[testing-loop-report-only]].
