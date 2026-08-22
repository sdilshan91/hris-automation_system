---
name: attendance-timezone-tc
description: How to write deterministic ISSUE-065 tenant-timezone regression tests for AttendanceService (clock-out is the only controllable seam) + TC-ATT-TZ-* scheme
metadata:
  type: project
---

Regression tests for the Phase 2b tenant-timezone work (`fix/phase2b-timezone`, ISSUE-065) live in
`src/backend/HRM.Tests/Unit/TenantClockTests.cs` (pure helper) + `Unit/AttendanceTenantTimezoneTests.cs`
(InMemory integration), doc `HRM.Tests/PHASE2B-TIMEZONE-TESTCASES.md`. TC ids: `TC-ATT-TZ-001..005`
(helper) / `TC-ATT-TZ-010..013` (attendance).

**Why:** attendance stores punches in UTC and only DERIVES day-boundary + late/early in the tenant's
local zone via `TenantClock` (pure static in `HRM.Application/Common/Helpers`). The UTC-default no-op is
the load-bearing safety property.

**How to apply (the non-obvious seam):**
- `AttendanceService.ClockInAsync` stamps the punch with live `DateTime.UtcNow` → NOT controllable, so
  you cannot assert a deterministic instant through clock-IN.
- `ClockOutAsync` RE-DERIVES `is_late`/`late_minutes` and the lock/shift date from the SEEDED open log's
  `ClockIn` (AttendanceService.cs ~L249, L284-L302). So: seed an OPEN `AttendanceLog` with an exact UTC
  `ClockIn`, seed a tenant-default fixed shift (09:00-17:00, grace 0, `IsDefault=true`) + a `Tenant` row
  with `TimeZone=`, then call `ClockOutAsync` and assert the persisted log.
- Discriminating assertion that defeats a UTC-only impl: assert the EXACT `LateMinutes` magnitude
  (`14:30Z`→NY 09:30 → 30, NOT 330) and the flip (`13:45Z`→NY 08:45 not-late, but a UTC tenant at the
  same instant IS late by 285). Day-boundary: `02:30Z`→NY 2026-01-14 (prev local day); prove via a
  period lock over 2026-01-14 → clock-out `409 payroll_period_locked` for NY, succeeds for UTC tenant.
- `America/New_York` resolves on this host (.NET 10 ICU). DST-gap (`LocalToUtc` for a spring-forward
  time) currently THROWS `ArgumentException` — pinned as ISSUE-251 baseline, nothing depends on it.
- Full suite green at 2998 pass / 0 fail (2026-07-08); the no-op property means no existing attendance
  test moved. RegularizationApprovalService still uses `TimeOnly.FromDateTime(log.ClockIn)` directly
  (NOT TenantClock) — regularization-approval late recompute is NOT tenant-zone-aware.
