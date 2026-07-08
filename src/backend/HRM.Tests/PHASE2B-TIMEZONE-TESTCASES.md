# Phase 2b Tenant-Timezone — IEEE-829 Regression Test Cases

Regression coverage for the tenant-timezone attendance work landed on `fix/phase2b-timezone`
(ISSUE-065: attendance day-boundary + late/early derivation keyed on the tenant's LOCAL time, with
UTC stored on disk unchanged). Test type: functional unit/integration tests (xUnit + EF InMemory +
real `TimeZoneInfo` — no mocked clock/zone). Each xUnit method carries a `@TC-*` binding comment
matching the **Test Case ID** below.

The load-bearing seam is the pure `TenantClock` helper; the attendance tests drive it through the real
`AttendanceService` clock-out path (the only path whose punch instant a test can control — clock-IN
stamps the live `DateTime.UtcNow`, clock-OUT re-derives late/day from the seeded open log's `ClockIn`).

| Test Case ID | Item | User Story / Issue | xUnit method(s) | File |
|---|---|---|---|---|
| TC-ATT-TZ-001 | UTC no-op (safety property) | US-ATT-002/008 / ISSUE-065 | `ResolveTimeZone_Utc_ReturnsUtc`, `LocalDateOf_/LocalTimeOfDay_/LocalToUtc_UtcZone_*`, `TodayIn_UtcZone_EqualsUtcNowDate` | `Unit/TenantClockTests.cs` |
| TC-ATT-TZ-002 | Non-UTC conversion | US-ATT-008 / ISSUE-065 | `ResolveTimeZone_NewYork_IsNotUtc`, `LocalDateOf_/LocalTimeOfDay_NewYork_*` | `Unit/TenantClockTests.cs` |
| TC-ATT-TZ-003 | Invalid/blank zone → UTC, never throws | ISSUE-065 (BR-5) | `ResolveTimeZone_InvalidOrBlank_FallsBackToUtcWithoutThrowing` | `Unit/TenantClockTests.cs` |
| TC-ATT-TZ-004 | `LocalToUtc` round-trip | US-ATT-008 / ISSUE-065 | `LocalToUtc_NewYork_RoundTripsToCorrectUtcInstant` | `Unit/TenantClockTests.cs` |
| TC-ATT-TZ-005 | DST-gap current-behavior baseline | ISSUE-251 | `LocalToUtc_NewYork_SpringForwardGap_CurrentlyThrows_ISSUE251Baseline` | `Unit/TenantClockTests.cs` |
| TC-ATT-TZ-010 | Late keyed on LOCAL time | US-ATT-008 / ISSUE-065 | `TenantClock_NonUtcTenant_LateByLocalTime_ISSUE065` | `Unit/AttendanceTenantTimezoneTests.cs` |
| TC-ATT-TZ-011 | On-time by LOCAL time (not late) | US-ATT-008 / ISSUE-065 | `TenantClock_NonUtcTenant_OnTimeByLocalTime_NotLate` | `Unit/AttendanceTenantTimezoneTests.cs` |
| TC-ATT-TZ-012 | UTC tenant unchanged (no-op e2e) | US-ATT-008 / ISSUE-065 | `TenantClock_UtcTenant_Unchanged_LateByUtcTime`, `TenantClock_UtcTenant_NearMidnight_UsesUtcDate_NotLocked` | `Unit/AttendanceTenantTimezoneTests.cs` |
| TC-ATT-TZ-013 | Day-boundary → LOCAL date | US-ATT-002/009 / ISSUE-065 | `TenantClock_NonUtcTenant_NearMidnight_AttributesToLocalDate` | `Unit/AttendanceTenantTimezoneTests.cs` |

---

## TC-ATT-TZ-001 — UTC zone is a byte-identical no-op (safety property)
- **Objective:** for `TimeZoneInfo.Utc`, every `TenantClock` conversion equals the plain
  `DateOnly.FromDateTime` / `TimeOnly.FromDateTime` / `date.ToDateTime(time, Utc)` — proving UTC-default
  tenants (and the whole pre-existing attendance suite) are unchanged.
- **Steps:** call `LocalDateOf`/`LocalTimeOfDay`/`LocalToUtc`/`TodayIn` with `TimeZoneInfo.Utc`.
- **Expected:** results identity-equal the plain conversions (`02:30Z`→`2026-01-15`/`02:30`;
  `LocalToUtc`→`2026-01-15T09:00:00Z` with `Kind=Utc`; `TodayIn`∈{before,after} UTC-now brackets).
- **Category:** Functional, Boundary (safety property).

## TC-ATT-TZ-002 — Non-UTC zone crosses the day-boundary and wall-clock correctly
- **Objective:** the ISSUE-065 derivation projects a UTC instant into the tenant zone.
- **Preconditions:** `America/New_York` resolves on the host (.NET 10 ICU).
- **Steps:** `LocalDateOf`/`LocalTimeOfDay` of `2026-01-15T02:30:00Z` under New York (winter UTC-5).
- **Expected:** local date `2026-01-14` (previous day) and local time `21:30`.
- **Category:** Functional.

## TC-ATT-TZ-003 — Invalid/blank zone id falls back to UTC without throwing
- **Objective:** a mis-configured tenant zone degrades gracefully instead of breaking clock-in.
- **Steps:** `ResolveTimeZone(null)`, `""`, whitespace, `"Not/AZone"`.
- **Expected:** each returns `TimeZoneInfo.Utc` and does not throw.
- **Category:** Negative, Boundary.

## TC-ATT-TZ-004 — `LocalToUtc` round-trips a non-UTC wall-clock
- **Objective:** a tenant-local date+time converts to the correct UTC instant and back.
- **Steps:** `LocalToUtc(2026-01-15, 09:30, NewYork)`; then project back.
- **Expected:** `2026-01-15T14:30:00Z`; back-projection recovers `2026-01-15` / `09:30`.
- **Category:** Functional.

## TC-ATT-TZ-005 — DST spring-forward gap currently throws (ISSUE-251 baseline)
- **Objective:** pin CURRENT behavior for a non-existent local wall-clock time so the ISSUE-251
  follow-up has a known baseline. **Documents current behavior only — not the desired end-state.**
- **Steps:** `LocalToUtc(2026-03-08, 02:30, NewYork)` (inside the skipped 02:00–02:59 hour).
- **Expected (current):** throws `ArgumentException`. Deterministic (fixed DST date); no other test
  depends on this.
- **Category:** Boundary (documenting-current-behavior).

## TC-ATT-TZ-010 — Late flagged by LOCAL time-of-day (ISSUE-065)
- **Objective:** a punch is judged late against the tenant-LOCAL wall-clock, not UTC.
- **Preconditions:** New York tenant; default 09:00–17:00 fixed shift, grace 0; open log
  `ClockIn=2026-01-15T14:30:00Z` (= 09:30 local).
- **Steps:** `ClockOutAsync` (re-evaluates late from the seeded clock-in).
- **Expected:** `is_late=true`, `late_minutes=30` (09:30 − 09:00). A UTC-only impl would persist `330`,
  so the exact `30` is the discriminating assertion.
- **Category:** Functional (fix verification).

## TC-ATT-TZ-011 — On time by LOCAL time is NOT late
- **Objective:** a punch that is before shift start in local terms is not flagged.
- **Preconditions:** as TC-ATT-TZ-010 but open log `ClockIn=2026-01-15T13:45:00Z` (= 08:45 local).
- **Expected:** `is_late=false`, `late_minutes=0`. A UTC-only impl (13:45 > 09:00) would have flagged
  late — so `false` proves local keying.
- **Category:** Functional, Boundary.

## TC-ATT-TZ-012 — UTC tenant unchanged (no-op end-to-end)
- **Objective:** the same instants under a UTC tenant reproduce the pre-ISSUE-065 behavior.
- **Steps:** clock-out with `ClockIn=13:45Z` (late by 285) and with `ClockIn=02:30Z` under a
  lock over `2026-01-14`.
- **Expected:** `is_late=true`/`late_minutes=285`; the near-midnight clock-out SUCCEEDS (UTC local date
  `2026-01-15` is outside the lock) — the opposite verdict to the New York tenant, proving zone-driven.
- **Category:** Functional (regression / no-op guard).

## TC-ATT-TZ-013 — Near-midnight punch attributed to the LOCAL date
- **Objective:** the day-boundary (used for period-lock / shift-date) keys on the local date.
- **Preconditions:** New York tenant; open log `ClockIn=2026-01-15T02:30:00Z` (= 21:30 on 2026-01-14
  local); active period lock over `2026-01-14`.
- **Steps:** `ClockOutAsync`.
- **Expected:** rejected `409 payroll_period_locked` — the punch falls on the LOCAL date `2026-01-14`,
  which is locked (a UTC tenant at the same instant is unlocked; see TC-ATT-TZ-012).
- **Category:** Functional, Boundary.
