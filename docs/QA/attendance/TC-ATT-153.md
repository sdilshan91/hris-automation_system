---
id: TC-ATT-153
user_story: US-ATT-006
module: Attendance
priority: high
type: integration
status: draft
created: 2026-07-15
defect:
  - BUG-285
---

# TC-ATT-153: Gulf tenant OT weekend basis follows the resolved work-week — Friday OT uses the weekend multiplier, Sunday OT the weekday multiplier (BUG-285 regression)

## 1. Test Objective
Verify the BUG-285 fix on US-ATT-006: `OvertimeMultiplierResolver` decides weekend-vs-weekday from the **resolved working-day set** (via `ShiftScheduleResolver`), not the hardcoded `Sat/Sun` check. For a Gulf tenant on a Sun–Thu work-week (`{7,1,2,3,4}`), **Friday** OT (their weekend) must earn the **weekend** multiplier and **Sunday** OT (a workday) the **weekday** multiplier. Assert the multiplier that lands in the stored payroll earnings.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: overtime multiplier resolution (weekend basis)
- Defect: BUG-285
- Cross-reference: US-ATT-011 FR-2/FR-7 (resolver is the single source of the work-week)

## 3. Preconditions
- Gulf employee resolving a Sun–Thu work-week (Location or tenant shift `{7,1,2,3,4}`).
- `AttendanceSettings` with distinct `WeekdayOvertimeMultiplier` (e.g. 1.5) and `WeekendOvertimeMultiplier` (e.g. 2.0).
- Postgres-backed context; OT events with fixed hours so stored earnings are assertable.
- Pre-fix: the resolver's literal `date.DayOfWeek is Saturday or Sunday` check makes this test FAIL (Friday billed weekday, Sunday billed weekend).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Work-week | `{7,1,2,3,4}` (Sun–Thu) | Fri/Sat = weekend |
| Weekday OT mult | 1.5 | applies to Sun–Thu |
| Weekend OT mult | 2.0 | applies to Fri/Sat |
| OT hours (each day) | e.g. 2h | fixed for earnings assert |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record/approve OT on a **Friday** for the Gulf employee. | Weekend multiplier **2.0** applied; stored OT earnings == base × 2h × 2.0. |
| 2 | Record/approve OT on a **Sunday** for the same employee. | Weekday multiplier **1.5** applied; stored OT earnings == base × 2h × 1.5. |
| 3 | Confirm both auto-detect and pre-approval OT paths use the resolved set. | Same multiplier basis on both paths. |

## 6. Postconditions
- OT pay reflects the true work-week; no hardcoded Sat/Sun basis remains for non-Mon–Fri tenants.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
