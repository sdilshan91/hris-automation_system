---
id: TC-PAY-014
user_story: US-ATT-011
module: Payroll
priority: critical
type: integration
status: automated
created: 2026-07-15
---

# TC-PAY-014: ExcludeHolidaysFromWorkingDays ON (opt-in) — a public holiday is not a working day, on BOTH sides of the pro-ration (AC-4)

> **⚠ Amended 2026-07-15 (CAL-5).** This TC previously read *"ON (default)"* and cited
> `AttendanceSettings.ExcludeHolidaysFromWorkingDays = true (default)`. Both were wrong. The flag is **OFF by
> default** and lives on a **per-tenant, effective-dated `TenantPayrollCalendarPolicy`**, not on
> `AttendanceSettings` (which is not effective-dated). This is a money decision (US-ATT-011 BR-9): defaulting it
> ON would have raised the OT hourly base — and the LOP daily rate — for **every existing tenant on their next
> payroll run**, contradicting the F&F precedent that a policy change applies next-cycle and is never
> retroactive. The default-off no-change guarantee is **TC-PAY-015**.

## 1. Test Objective
Verify US-ATT-011 AC-4 / FR-5 / BR-10 / BR-11: when a tenant has turned the effective-dated policy **on**, a
public holiday is **not a working day** — excluded from the pro-ration **denominator** AND the paid-days
**numerator** — so the OT hourly base and the LOP daily rate both rise, and holidays are scoped to the
employee's Location.

## 2. Related Requirements
- User Story: US-ATT-011 · Acceptance Criteria: AC-4 · Functional Requirement: FR-5
- Business Rules: BR-9 (off by default, effective-dated) · BR-10 (single-basis) · BR-11 (location-scoped)
- Cross-reference: `PayrollRunProcessor.ProRataPaidDays` (the shift-aware NUMERATOR, PR #282)

## 3. Preconditions
- A `TenantPayrollCalendarPolicy` version with `ExcludeHolidaysFromWorkingDays = true`, `EffectiveFrom` on or before the pay period.
- An employee whose Location has **2** public holidays falling on working days; BASIC 22,000; a 22-working-day month.
- Postgres-backed context; a real payroll run so the stored figures are assertable.

## 4. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run payroll for the month. | Working days == **N − 2** (22 → 20). |
| 2 | Compare the OT amount against the same run with the flag off. | The hourly base rises (`22000/(20×8)` vs `22000/(22×8)`) → the SAME approved minutes earn **more**. |
| 3 | Compare the LOP deduction against the flag-off run. | The daily rate rises (`22000/20` vs `22000/22`) → the SAME LOP days deduct **more**. *(Named honestly: this flag makes LOP cost the employee more — it is not purely an employee benefit.)* |
| 4 | Run for a **mid-month joiner**. | The pro-ration factor uses holiday-excluded days on **BOTH** sides (BR-10). Excluding them from the denominator only would over-pay — single-basis must hold. |
| 5 | A holiday at Location A. | Does **not** reduce a Location-B employee's working days (BR-11). |

## 5. Postconditions
- Payroll working-day counting matches leave day-counting for tenants that opted in; untouched for those that did not.

## 6. Notes
Holidays resolve via `IHolidayProvider(employee.LocationId)` — the same location-aware source overtime uses
(CAL-3 / BUG-286) — batched once per DISTINCT location per run, never per employee.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `PayrollWorkingDaysDenominatorTests.FlagOn_DenominatorExcludesHolidays` (step 1)
  - `PayrollWorkingDaysDenominatorTests.FlagOn_OvertimeHourlyBaseRises_SameMinutesEarnMore` (step 2 — exact figures)
  - `PayrollWorkingDaysDenominatorTests.FlagOn_LopDailyRateRises_SameLopDaysDeductMore` (step 3 — exact figures)
  - `PayrollWorkingDaysDenominatorTests.FlagOn_SingleBasisHeld_JoinerProRationUsesHolidayExcludedDaysOnBothSides` (step 4 — **the money trap**)
  - `PayrollWorkingDaysDenominatorTests.FlagOn_HolidayAtOneLocation_DoesNotReduceAnotherLocationsDenominator` (step 5)
- **Mutation-verified:** dropping holidays from `ProRataPaidDays` (the numerator) while keeping them in the denominator — the bug the original plan wording would have produced — reddens the step-4 arm.
- Backing suite trait: `[Trait("TC", "TC-PAY-014")]`.
