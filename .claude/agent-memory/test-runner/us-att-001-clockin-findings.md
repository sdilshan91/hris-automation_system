---
name: us-att-001-clockin-findings
description: 2026-06-25 API test pass of US-ATT-001 clock-in (16 owned TCs, 13P/3B); route, persona, settings recipe, and 6 new findings
metadata:
  type: project
---

# US-ATT-001 clock-in test pass (2026-06-25, REPORT-ONLY API)

Route **POST /api/v1/attendance/clock-in**, permission **`Attendance.CheckIn`** (NOT the spec's `Attendance.Clock.Self` — drift, see [[ISSUE-068]]). Controller `AttendanceController.cs:36`; logic `AttendanceService.ClockInAsync` (`HRM.Infrastructure/Services/AttendanceService.cs:49`). Table is **`attendance_log`** (singular). Clock-out with `{}` body resets state (but needs coords when geo required). `GET /api/v1/attendance/status` = own/tenant-scoped read.

**Verdicts (16 owned: TC-ATT-001..012 + ISO-001..004):** 13 PASS, 3 BLOCKED. Non-owned cross-ref left draft: TC-ATT-055/100/138, ISO-007..013.
- PASS: 001 (happy, DB-verified IP/UA/tenant/created_by), 002 (no-geo optional), 003 (dup 409 exact msg), 004 (geo-required block 400 + coords 201), 005 (IP allowlist 403/201, exact-match only), 007 (geofence Haversine boundary inclusive), 008 (tenantadmin lacks CheckIn → 403), 009 (no/bad/empty/tampered token → 401), 010 (perf representative p95 35ms ≪500ms — single-client, NOT 50-VU load), 012 (race: DB partial-unique `ix_attendance_log_open_unique` → 201+409, idempotency-key replay = same id), ISO-001 (status own-only), ISO-002 (no-header 400 / ghost 404 / suspended 451 / cross-tenant=no-employee 403), ISO-003 (body tenantId/employeeId injection IGNORED, stamped acme/John).
- BLOCKED: 006 (can't inject clock-in time — server uses `UtcNow`; logic flaw filed [[ISSUE-065]]), 011 (fe-platform-bound a11y/responsive), ISO-004 (deferred — Redis cache FR-6 not wired; DB-path isolation holds).

**Cross-tenant WRITE arm is CLEAN here** (unlike BUG-003 read class): acme JWT + `X-Tenant-Subdomain: techoneglobal` → 403 "No employee record linked" because employee resolution `Employees.First(UserId==CurrentUser.UserId)` runs under the target tenant's query filter → no acme employee in techoneglobal → no write. 0 techoneglobal rows created. ClockInRequest DTO only has lat/lng/photo/source — tenant/employee structurally un-injectable.

**6 new findings:** **BUG-047 MED** concurrent clock-in race → 500 (unhandled 23505 on main SaveChanges line 172; fix pattern already exists in same file `TryRecordIdempotencyAsync:563`; live log RequestId 0HNMIFE5GI37F proves it, my serialized burst gave clean 409). **ISSUE-065 MED** late-detection uses UTC time-of-day vs naive shift start, NO tenant-tz convert (`TimeOnly.FromDateTime(UtcNow)` line 158; LateEarlyCalculator docstring admits "tenant-timezone deferral") → 484min-late at 17:01Z vs 09:00 shift. **ISSUE-066 MED** IP allowlist `.Contains(ip)` exact-match, no CIDR; reads `RemoteIpAddress` not X-Forwarded-For. **ISSUE-067 LOW** clock-in writes NO audit_logs row (only Serilog) though infra active (325 rows/day other actions). **ISSUE-068 LOW** single geofence center (FR-3 "any allowed location" unmet) + perm-name drift. **ENH-003** DTO omits tenant_id/ip/ua (persisted but not returned).

**Settings recipe (acme tenant `019ef3ba-…`, settings id 019effac-…):** `UPDATE attendance_settings SET require_geolocation=?, geo_fence_enabled=?, geo_fence_latitude=?, geo_fence_longitude=?, ip_allowlist_enabled=?, ip_allowlist='{...}', require_photo=? WHERE tenant_id='019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9';` — RESTORED all to false/empty after run. Default "General Shift" 09:00-17:00 grace15 isDefault. psql at `/c/Program Files/PostgreSQL/18/bin/psql.exe`, db hris_dev_db, developer/Sanjesi#123.

**RESIDUE:** ~33 closed attendance_log rows for John Doe (`019efced-88a9-…`) in acme today (all clock_out set, none open). Flag for cleanup. NO techoneglobal writes. employee@acme.test = John Doe (linked). See [[testing-loop-report-only]] [[qa-no-debugger-for-perf]] [[qa-personas-reseed-2026-06-25]]
