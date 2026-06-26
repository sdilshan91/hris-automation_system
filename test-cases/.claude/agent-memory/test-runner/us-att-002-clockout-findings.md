---
name: us-att-002-clockout-findings
description: 2026-06-25 API pass of US-ATT-002 employee clock-out — 10 PASS / 3 BLOCKED; worked-hours calc + auto-clock-out job + isolation all solid; only LOW audit/DTO/test-data nits
metadata:
  type: project
---

US-ATT-002 (employee clock-out + work-hours auto-calc) REPORT-ONLY API run, 2026-06-25.

**Owned TCs (13): 10 PASS / 3 BLOCKED.** Verdicts: TC-ATT-013/014/015/016/017/018/019/020/021/ISO-005 PASS; TC-ATT-022 (atomicity) BLOCKED fault-injection-unavailable; TC-ATT-023 (perf k6) BLOCKED load-not-seedable (acme has only 1 employee-linked persona; single-thread probe p95=58ms << 500ms NFR-1); TC-ATT-024 (a11y/cross-browser) BLOCKED fe-platform-bound. Cross-ref non-owned (left draft): TC-ATT-067/094/105/115/138.

**New findings: ISSUE-069, ISSUE-070, ENH-004 (all LOW). No new BUGs.**
- ISSUE-069: clock-out writes NO audit_logs row (twin of ISSUE-067 clock-in; only Serilog INF + row updated_by). Plus no-open-record reject returns HTTP 404 (TCs expected 409/422) but exact AC-2 message + code `no_active_clock_in` is correct.
- ISSUE-070 (TEST layer): TC-017/018 assume 6h (360m) short-day minimum, live `attendance_settings.minimum_work_minutes`=240 (4h). Engine is CORRECT vs seeded config (180m→SHORT_DAY, 240m→COMPLETE); TC test-data drift.
- ENH-004: ClockOutResultDto omits tenant_id/clock_out_ip/lat-long (persisted correctly, presentation gap; parity w/ ENH-003).

**Worked-hours calc verdict: CORRECT.** `AttendanceCalculator.Calculate` (pure, Domain). Math: net = floor((out-in)min) - break; break=60 iff gross>360 (strictly >, verified 360→no break/361→60). OT = max(0, net-(480+0)). Status precedence ANOMALY(>16h gross or system-closed) > SHORT_DAY(<min) > OVERTIME(>0) > COMPLETE. Verified live: 525raw→465 COMPLETE; 660raw→600 net OT120 OVERTIME (+ separate overtime_record PENDING 180m via US-ATT-006); 180→SHORT_DAY; 360→360/361→301; 960raw→OVERTIME(not anomaly)/961raw→ANOMALY. Exact to the minute (NFR-2). **clock_out = server UtcNow only** (client cannot inject time → backdate `clock_in` in DB to get exact durations: `update attendance_log set clock_in = now() - interval 'N minutes'`).

**clock-out-without-clock-in: CORRECT** — 404 `no_active_clock_in`, exact AC-2 message, no row created; double clock-out on closed record also rejected, original untouched (no double-count).

**Audit verdict:** only updated_at/updated_by on the row (NFR-3 fields ok); NO audit_logs action entry (ISSUE-069).

**Isolation verdict: CLEAN — clock-out is NOT BUG-003-exploitable.** Resolves record via ICurrentUser.UserId→employee→EF-filtered open log; never via client id. acme JWT + techoneglobal subdomain → 403 "no employee linked"; body inject tenantId/employeeId IGNORED (ClockOutCommand only has lat/long/ip/ua) — closed John's OWN record. techoneglobal attendance_log = 0 rows. ZERO cross-tenant writes.

**Auto-clock-out Hangfire job (TC-021): WORKS.** Recurring `attendance-auto-clock-out` (cron `5 0 * * *`); trigger on-demand: `curl -X POST http://localhost:5000/hangfire/recurring/trigger --data "jobs[]=attendance-auto-clock-out" -H "X-Requested-With: XMLHttpRequest"` → 204. Closes only prior-UTC-day open records (ClockIn < todayStartUtc) at clock-in-day 23:59:59 UTC, status ANOMALY, updated_by=system, control record untouched, idempotent (re-run no change). BR-5 says tenant-tz 23:59 but uses UTC end-of-day (documented deferral, ISSUE-065 tz theme — referenced not re-filed).

**Stack facts:** API :5000; psql at `/c/Program Files/PostgreSQL/18/bin`, conn `developer`/`Sanjesi#123`/`hris_dev_db` (pw in user-secrets, NOT appsettings). Table is singular `attendance_log`. John Doe employee `019efced-88a9-7825-a8e0-7571318deb74`, acme `019ef3ba-…`. Live acme settings: standard 480, min 240, break 60/>360, OT-threshold 0, geo off.

**acme residue:** John now has 87 attendance_log rows (was 33; +54 create/close cycles, all CLEAN-CLOSED, 0 dangling open). attendance_settings.require_geolocation flipped true then RESTORED to false. Flag for cleanup; no defect. No ERR/FTL in Serilog during run.

See [[testing-loop-report-only]] [[qa-no-debugger-for-perf]] [[qa-personas-reseed-2026-06-25]]
