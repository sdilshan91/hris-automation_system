---
id: TC-ATT-152
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-152: FteScaledOvertimeBase flag — OT hourly base unscaled by default, scaled by FTE when on (AC-5)

## 1. Test Objective
Verify US-ATT-011 AC-5 / FR-6: with `FteScaledOvertimeBase` **off (default)**, a 0.5-FTE part-timer's overtime hourly base equals a full-timer's (base NOT scaled by FTE); with the flag **on**, the OT hourly base scales by FTE (`standardHours * Fte`). Depends on `Employee.Fte` (US-CHR-013).

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-5
- Functional Requirement: FR-6
- Business Rule: BR-6 (`FteScaledOvertimeBase` defaults off, backward-compatible)
- Dependency: US-CHR-013 (`Employee.Fte`)

## 3. Preconditions
- A part-time employee with `Fte = 0.5` and a comparable full-time employee (`Fte = 1.0`) on the same salary/standard-hours.
- `AttendanceSettings.FteScaledOvertimeBase = false` initially (tenant default).
- Postgres-backed context; OT event with known hours so the stored earnings are assertable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Part-timer Fte | 0.50 | half-time |
| Full-timer Fte | 1.00 | control |
| FteScaledOvertimeBase | false → true | toggled between arms |
| standardHours | e.g. 160/mo | base derivation input |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Flag **off**: compute the part-timer's OT hourly base for an OT event. | Base == the full-timer's base (NOT halved) — FTE ignored for OT base. |
| 2 | Set `FteScaledOvertimeBase = true`; recompute the part-timer's OT base. | Base scales by FTE (`standardHours * 0.5`) → part-timer's per-hour base is higher (fewer standard hours in the divisor) per the spec's `standardHours * Fte` rule; assert the stored OT earnings differ from arm 1. |
| 3 | Full-timer under both arms. | Unchanged (Fte = 1.0 is a no-op). |

## 6. Postconditions
- The flag defaults off (no behaviour change); when on, only part-timers' OT base is affected.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `OvertimeFteBaseTests.FlagOff_FteIsIgnored_HourlyRateIsUnchanged` (**the control** — flag OFF is the default; FTE must not touch the base)
  - `OvertimeFteBaseTests.DefaultCallShape_WithoutFteArguments_IsUnchanged` (pins the trailing-optional defaults themselves)
  - `OvertimeFteBaseTests.FlagOn_HalfFte_HourlyRateIsExactlyDouble` (0.5 FTE → exactly 2x, not merely greater)
  - `OvertimeFteBaseTests.FlagOn_FullTimeEmployee_IsIdenticalToFlagOff` (turning it on must not disturb full-timers)
  - `OvertimeFteBaseTests.FlagOn_NonPositiveFte_FallsBackToTheUnscaledBase_NeverThrowsOrGoesNegative` (a corrupt FTE must not divide-by-zero or invert pay)
  - `AttendancePolicyResolverTests.LazyCreatedTenantDefault_HasFteScaledOvertimeBaseOff` — **added after mutation-testing found the gap**: every other arm passes the flag EXPLICITLY, so flipping the entity initializer to `true` survived the whole suite, yet EF sends it on INSERT for a lazily-created row.
- Backing suite trait: `[Trait("TC", "TC-ATT-152")]`.
