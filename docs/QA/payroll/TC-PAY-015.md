---
id: TC-PAY-015
user_story: US-ATT-011
module: Payroll
priority: critical
type: integration
status: automated
created: 2026-07-15
---

# TC-PAY-015: ExcludeHolidaysFromWorkingDays OFF (the DEFAULT) — holidays count as working days and NO figure changes (AC-4 / BR-9 no-regression guarantee)

> **⚠ Amended 2026-07-15 (CAL-5).** This TC previously described OFF as "the opposite of the default". OFF **IS**
> the default. This arm is therefore not a negative curiosity — it is **the guarantee the whole design rests on**:
> every existing tenant, having no policy row, must see payroll figures that are byte-identical to the pre-CAL-5
> engine. It is the reason the flag is opt-in and effective-dated rather than defaulted on (BR-9).

## 1. Test Objective
Verify US-ATT-011 AC-4 / FR-5 / BR-9: with **no policy row** (the code-default, and every existing tenant),
public holidays are **not** subtracted — they count as working days — and the working-days count, the OT amount
and the LOP deduction are all **identical** to a month with no holidays at all. Also verifies that a tenant that
configured the flag OFF explicitly behaves the same.

## 2. Related Requirements
- User Story: US-ATT-011 · Acceptance Criteria: AC-4 · Functional Requirement: FR-5
- Business Rule: BR-9 (off by default, effective-dated — a money decision, never retroactive)

## 3. Preconditions
- Same month/employee as TC-PAY-014, but `AttendanceSettings.ExcludeHolidaysFromWorkingDays = false` (via tenant default or a location override).
- Postgres-backed context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Working days | N | resolver |
| Public holidays on workdays | 2 | present but NOT excluded |
| Flag | false | holidays count |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set the flag off and run payroll for the month. | Denominator == **N** (the 2 holidays are counted as working days). |
| 2 | Diff against TC-PAY-014's on-flag result for the same month. | Denominators differ by exactly 2 — the flag is the only cause. |
| 3 | Set the flag via a **location override** (not tenant default). | The override governs only that location's employees; tenant-default employees keep their own flag value. |

## 6. Postconditions
- The denominator responds to the flag; location override respected.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `PayrollWorkingDaysDenominatorTests.NoPolicyRow_HolidaysDoNotChangeAnyFigure_DefaultIsOff` — **the control arm**: a month with 2 holidays produces the SAME working days, OT amount and LOP amount as a month with 0 holidays. This is the no-regression guarantee for every existing tenant.
  - `PayrollWorkingDaysDenominatorTests.NoPolicyConfigured_GetEffective_ReportsCodeDefaultOff` — the effective policy surfaces the code-default (off) so the behaviour is visible without seeding.
  - `PayrollWorkingDaysDenominatorTests.EffectiveDating_MayResolvesTheOffVersion_JulyResolvesTheOnVersion` — a change is never retroactive: a May period keeps the off version after a June-effective on version is added.
- **Mutation-verified:** flipping the code-default in `PayrollCalendarResolver.ExcludeHolidaysAsync` from `?? false` to `?? true` reddens the control arm. (Note the *entity* initializer default is NOT the guarantee — the resolver's no-row fallback is; mutating the entity default alone leaves every arm green.)
- Backing suite trait: `[Trait("TC", "TC-PAY-015")]`.
