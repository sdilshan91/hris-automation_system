---
id: TC-ATT-092
user_story: US-ATT-007
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-092: Holiday and weekly-off exclusion -- public holidays and weekly offs are not counted as present or absent (BR-4)

## 1. Test Objective
Verify BR-4: public holidays and weekly offs are excluded from present/absent calculations. A public holiday falling on a weekday and a weekly-off day each appear in their own summary columns (total_holidays / total_weekly_offs) and are NOT counted as present, absent, or LOP, regardless of whether the employee clocked in.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-3 (total_holidays, total_weekly_offs)
- Business Rules: BR-2 (absent = scheduled working day), BR-4 (holiday/weekly-off excluded)
- Data Requirements: §7 total_holidays, total_weekly_offs

## 3. Preconditions
- Tenant "acme". HR Officer authenticated with `Attendance.Read.All`.
- Month 2026-05 has 2 public holidays on weekdays and the employee's shift defines weekly-offs (e.g. Sat/Sun). Employee "Hari": no clock-in on the holidays/weekly-offs; normal attendance otherwise.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| public holidays (weekday) | 2 | from holiday calendar |
| weekly-offs | per shift working_days | US-ATT-005 |
| expected | holidays/weekly-offs not absent, not present | BR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate the 2026-05 summary for Hari | total_holidays = 2 and total_weekly_offs reflects the month's weekly-offs for his shift. |
| 2 | Verify holidays are excluded from absent | The 2 weekday holidays (no clock-in) do NOT increment total_absent_days or lop_days. |
| 3 | Verify weekly-offs are excluded | Weekly-off days (no clock-in) do NOT increment absent or LOP. |
| 4 | Verify holidays/weekly-offs are not counted present | They do not add to total_present_days either (BR-4 -- excluded both ways). |
| 5 | Drill down on Hari | Holiday days show status = holiday; weekly-off days show status = weekly-off. |
| 6 | Employee who DID clock in on a holiday | Handled per the documented rule (e.g. counted as overtime/holiday-work, not as a normal present day); assert against the implemented behavior and flag if unspecified. |

## 6. Postconditions
- Public holidays and weekly offs are tracked in their own columns and excluded from present/absent/LOP; only scheduled working days drive present/absent.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Public-holiday classification requires the holiday calendar (US-LV-007, implemented) integrated into the attendance summary; weekly-offs derive from the shift working_days (US-ATT-005). If the holiday-source integration into the summary computation is not yet wired, the holiday-exclusion steps are CONDITIONAL on that integration (weekly-off exclusion passes independently). **Reported to caller.**
- Behavior for clocking-in on a holiday/weekly-off (holiday-work) overlaps US-ATT-006 overtime multipliers; assert against the documented rule.
