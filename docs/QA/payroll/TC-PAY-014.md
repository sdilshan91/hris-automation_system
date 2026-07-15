---
id: TC-PAY-014
user_story: US-ATT-011
module: Payroll
priority: high
type: integration
status: draft
created: 2026-07-15
---

# TC-PAY-014: ExcludeHolidaysFromWorkingDays ON (default) reduces the payroll working-days denominator by the public-holiday count (AC-4)

## 1. Test Objective
Verify US-ATT-011 AC-4 / FR-5: with `ExcludeHolidaysFromWorkingDays = true` (default), a month containing **2 public holidays** yields a payroll working-days denominator reduced by **2** — aligning the payroll `workingDays * 8` hourly-rate base with leave day-counting (holidays excluded).

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-4
- Functional Requirement: FR-5
- Business Rule: BR-6 (flag defaults on)
- Cross-reference: `PayrollRunProcessor.ProRataPaidDays` (shift-aware denominator, PR #282)

## 3. Preconditions
- Tenant `AttendanceSettings.ExcludeHolidaysFromWorkingDays = true` (default).
- A month whose resolved work-week contains **N** working days, with **2** public holidays (via `IHolidayProvider`) falling on working days.
- Postgres-backed context; a payroll run so the stored denominator/base is assertable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Working days (no holiday) | N | from resolver |
| Public holidays on workdays | 2 | location-scoped |
| Flag | true (default) | exclude holidays |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run payroll for the month for an employee whose location has 2 holidays. | Working-days denominator == **N − 2**; hourly-rate base uses `(N−2) * 8`. |
| 2 | Compare against the same month with 0 holidays. | Denominator differs by exactly 2. |
| 3 | Confirm holidays are resolved via `IHolidayProvider(employee.LocationId)`. | Only that location's holidays reduce the denominator (ties to BUG-286 unification). |

## 6. Postconditions
- Payroll denominator matches leave day-counting when the flag is on.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
