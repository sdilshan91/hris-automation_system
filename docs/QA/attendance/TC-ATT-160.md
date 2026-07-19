---
id: TC-ATT-160
user_story: US-ATT-003
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-19
defect:
  - ISSUE-072
---

# TC-ATT-160: Regularization future-date guard is a coarse date-only frame in the validator; tenant-local rejection deferred to the service (ISSUE-072 — BR-4)

## 1. Test Objective
Verify the ISSUE-072 fix on US-ATT-003: `SubmitRegularizationValidator` applies the future-date check as a **coarse, date-only** guard (`Date <= UtcToday + 1 day` tolerance) instead of combining the wall-clock `HH:mm` with the date as UTC and comparing to `DateTime.UtcNow`. The old rule wrongly rejected valid **local-past** times for tenants ahead of UTC (up to +14h); the authoritative, tenant-local future-date rejection (with the `future_date` code, via `TenantClock.TodayIn`) now lives in the service. This TC asserts the validator no longer rejects a same-day/today request purely on a wall-clock frame, while a date clearly beyond the tolerance is still rejected.

## 2. Related Requirements
- User Story: US-ATT-003
- Business Rule: BR-4 (a regularization request cannot be for a future date)
- Functional Requirement: FR-5 (shape consistency — clock-in before clock-out)
- Finding: ISSUE-072 (PR #371)

## 3. Preconditions
- `SubmitRegularizationValidator` under test in isolation (FluentValidation `TestValidate`), no DB.
- The service-layer tenant-local `future_date` rejection is covered separately by the attendance service tests (out of scope for this validator-frame TC).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Today (UTC) date | `UtcToday` | within tolerance → accepted by the coarse guard |
| Tolerance | +1 day | `Date <= UtcToday.AddDays(1)` |
| Future date | `UtcToday.AddDays(2+)` | beyond tolerance → rejected shape-level |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Validate a request whose `Date` is today (UTC), regardless of the `HH:mm` corrected times. | No future-date validation error — the coarse date-only guard does not reject a same-day request on a wall-clock frame. |
| 2 | Validate a request whose `Date` is beyond the +1 day tolerance. | Shape-level failure: "The corrected date cannot be in the future." |
| 3 | (Service-layer, cross-reference — NOT this TC) A future date in the tenant's local calendar. | Rejected with `future_date` in `AttendanceService.SubmitRegularizationAsync`. |

## 6. Postconditions
- The validator's future-date guard is frame-independent (date-only + tolerance); tenants ahead of UTC are no longer falsely blocked on valid local-past corrections.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test (UTC-day +1 tolerance boundary)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):** `HRM.Tests/Unit/SubmitRegularizationValidatorTests.cs`
  - `FutureDate_BeyondOneDayTolerance_IsRejected_ISSUE072` — a date at `UtcToday + 2` trips the future-date error.
  - `TodayAndTomorrowTolerance_NotRejectedOnAWallClockFrame_ISSUE072` — a today request with a late corrected
    time (`23:00`/`23:30`) is NOT rejected (the old wall-clock-combined-as-UTC rule could have rejected it),
    and the `UtcToday + 1` tolerance boundary is also accepted.
- Both arms carry `[Trait("TC", "TC-ATT-160")]`. The bound regression arm was added with this backfill (the
  pre-existing validator arms use a fixed past date + only exercise FR-5/BR-7/§7 shape rules, so none probed
  the ISSUE-072 date-only future frame).
