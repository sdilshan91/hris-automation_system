---
id: TC-PAY-015
user_story: US-ATT-011
module: Payroll
priority: medium
type: integration
status: draft
created: 2026-07-15
---

# TC-PAY-015: ExcludeHolidaysFromWorkingDays OFF — public holidays COUNT in the payroll working-days denominator (AC-4 negative arm)

## 1. Test Objective
Verify US-ATT-011 AC-4 / FR-5 / BR-6: with `ExcludeHolidaysFromWorkingDays = false`, public holidays are **not** subtracted — they count as working days in the payroll denominator (`workingDays * 8`), the opposite of the default. Proves the flag actually governs the denominator.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-4
- Functional Requirement: FR-5
- Business Rule: BR-6

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
