---
name: us-att-003-regularization-findings
description: 2026-06-26 API pass on US-ATT-003 attendance regularization — create/validation/isolation all solid; only audit gap + error-code nits
metadata:
  type: project
---

# US-ATT-003 Regularization Request — test pass 2026-06-26

REPORT-ONLY API run, 13 owned TCs: **9 PASS / 1 FAIL / 3 BLOCKED**. Backend was DOWN at start (no dotnet listening on :5000); I started it native via `ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5000` (conn string + JWT key in user-secrets; PG18 on :5432; psql at `/c/Program Files/PostgreSQL/18/bin/psql.exe`, password `Sanjesi#123`).

**Why:** regularization create/validation/ownership/isolation are well-built; the only spec gaps are the systemic audit theme + minor error-envelope nits.
**How to apply:** for US-ATT-004 (manager approve/reject) re-use these personas/dates; the approve/reject paths likely share the same missing-`audit_logs` gap (ISSUE-071 names submit only, but NFR-3 covers all 3 actions) — verify there too.

## Routes & rules (verified)
- `POST /api/v1/attendance/regularizations` (gate `Attendance.Regularize.Self`), `GET …/regularizations` (own list).
- Employee resolves via `CurrentUser.UserId → employee` + EF `TenantId` filter → **isolation self-protected, NOT BUG-003** (re-confirmed: acme-emp token + `X-Tenant-Subdomain:e2e` → 403 "no employee record" on both GET and POST).
- Lookback default 7 days; earliest allowed = today-6 (today 2026-06-26 → 2026-06-20 OK, 06-19 rejected). Exact msgs: lookback "Regularization requests can only be submitted for the last 7 days." (code `lookback_exceeded`), duplicate "A pending regularization request already exists for this date." (code `duplicate_pending`), locked "This date falls within a locked payroll period. Please contact HR." (code `payroll_period_locked`).
- Validator (FluentValidation) fires BEFORE service: reason ≥10, type ∈ {MISSED_CLOCK_IN/OUT/BOTH}, conditional times required + "HH:mm" only, in<out, combined date+time not future. These return `code:null` (only service-layer policy rejections carry codes).
- MISSED_CLOCK_OUT links existing `attendance_log` for the date (AttendanceLogId set); approval (US-ATT-004) is what mutates the log.

## Findings filed
- **ISSUE-071 MED BE** — submit writes NO `audit_logs` row (only Serilog + interceptor field-stamp); NFR-3 explicitly requires it → MED not LOW. Twin of ISSUE-067/069 (attendance writes skip audit_logs).
- **ISSUE-072 LOW BE** — (a) validator rejections have no machine `code`; (b) service `future_date` code is unreachable (validator future-TIME rule shadows it since all types require a time). Future dates still correctly 400.
- No BUG-048: nothing broken vs spec. BUG-003 NOT extended.

## Verdicts
PASS: 025,026,027,028,029,030,031,036,ISO-006. FAIL: 033 (audit→ISSUE-071). BLOCKED: 032 (notification DEFERRED, code says no NTF infra), 034 (perf — load-not-seedable per [[us-att-002-clockout-findings]]; single-user 15-150ms), 035 (a11y — fe-platform-bound).

## acme residue (created this run)
3 PENDING regularizations for John (`019efced-…`): dates 2026-06-26 (MISSED_CLOCK_OUT, linked to log …021), 2026-06-24, 2026-06-20 (both MISSED_BOTH). Plus 1 inserted/relocated open attendance_log `019f0200-0000-7000-8000-000000000021` now on 2026-06-26 01:00. Pre-existing residue from prior run: pending regs on 06-25/23/22/19, REJECTED on 06-22, an ACTIVE period-lock on exactly 2026-06-21 (used as TC-029 evidence), and ~87 attendance logs on 06-25. NO writes to techoneglobal/other tenants.

See [[testing-loop-report-only]] [[qa-no-debugger-for-perf]] [[qa-personas-reseed-2026-06-25]]
