---
id: TC-ATT-148
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-148: Four-tier resolution — EU 4-day-week Location (`{1,2,3,4}`) resolves correct working-day count (AC-2b)

## 1. Test Objective
Verify US-ATT-011 AC-2 / FR-2: an employee at an EU Location whose `DefaultShiftId` is a 4-day shift (`WorkingDays = {1,2,3,4}` = Mon–Thu) resolves a 4-day working week — Friday, Saturday, and Sunday are all non-working — proving the resolver honours an arbitrary working-day set, not just Mon–Fri.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-2
- Functional Requirement: FR-2
- Test hint (US-ATT-011 §11): 4-day EU tenant working-day counts correct

## 3. Preconditions
- EU Location with `DefaultShiftId` = 4-day shift `{1,2,3,4}`.
- Employee at the EU Location with no personal shift.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Working days | `{1,2,3,4}` | Mon–Thu |
| Fri (5), Sat (6), Sun (7) | not in set | non-working |
| Calendar week | Mon–Sun | count check |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve the working-day set for the EU employee. | Set == `{1,2,3,4}`. |
| 2 | Count working days across a full Mon–Sun calendar week. | Exactly **4** working days. |
| 3 | Ask "is Friday a working day?" | FALSE. |

## 6. Postconditions
- No state change; count is exactly 4 per week for the EU population.

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
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `ShiftScheduleResolverLocationTierTests.Resolver_LocationTier_EuFourDayWeek_CountsExactlyFourWorkingDays`
- Backing suite trait: `[Trait("TC", "TC-ATT-148")]`.
