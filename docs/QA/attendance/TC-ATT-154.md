---
id: TC-ATT-154
user_story: US-ATT-006
module: Attendance
priority: medium
type: integration
status: automated
created: 2026-07-15
defect:
  - BUG-286
---

# TC-ATT-154: Location-scoped holiday OT — a New-York-only holiday does NOT grant a London employee the holiday OT multiplier (BUG-286 regression)

## 1. Test Objective
Verify the BUG-286 fix on US-ATT-006 (holiday scope, US-LV-007): `OvertimeService.IsPublicHolidayAsync` routes through the location-aware `IHolidayProvider(employee.LocationId)` instead of an unfiltered query, so a holiday defined **only for New York** does not grant a **London** employee the holiday OT multiplier, and a London-only holiday does not grant a New-York employee one.

## 2. Related Requirements
- User Story: US-ATT-006 (holiday OT basis)
- Cross-reference: US-LV-007 (location-scoped holiday calendar), US-ATT-011 FR-7 (unified `IHolidayProvider`)
- Defect: BUG-286

## 3. Preconditions
- One tenant with two Locations (New York, London) and a public holiday defined for **New York only** on date H.
- A New-York employee and a London employee, both with OT on date H.
- Postgres-backed context; assert the stored OT earnings/multiplier.
- Pre-fix: the unfiltered `Date == H && IsActive && Type == Public` query grants BOTH employees the holiday multiplier → this test FAILS for London.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Holiday H | New York location only | location-scoped |
| Holiday OT mult | e.g. 2.5 | applies only to NY employees |
| London employee | LocationId = London | must NOT get holiday mult on H |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record OT on date H for the **New York** employee. | Holiday OT multiplier applied (H is a NY holiday). |
| 2 | Record OT on date H for the **London** employee. | Holiday multiplier **NOT** applied; London employee gets the ordinary weekday/weekend multiplier (H is not a London holiday). |
| 3 | Define a London-only holiday and repeat for a NY employee. | NY employee does not get the holiday multiplier (symmetry). |

## 6. Postconditions
- Holiday OT is location-correct; leave/attendance/overtime/payroll all consult the same `IHolidayProvider(locationId)`.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `OvertimeWorkWeekAndHolidayScopeTests.NewYorkOnlyHoliday_GrantsHolidayRateToNyEmployee_ButNotToLondonEmployee` (steps 1–2 — both employees on the tenant Mon–Fri default and the date is a weekday, so ONLY holiday scope can move the multiplier)
  - `OvertimeWorkWeekAndHolidayScopeTests.LondonOnlyHoliday_DoesNotGrantHolidayRateToNewYorkEmployee` (step 3 — symmetry)
  - `OvertimeWorkWeekAndHolidayScopeTests.TenantWideHoliday_StillReachesEveryLocation` (proves the fix narrowed scope WITHOUT breaking the ordinary company-wide holiday)
- **Mutation-tested:** dropping `locationId` from the holiday lookup → 2 arms red, tenant-wide control green.
- Backing suite trait: `[Trait("TC", "TC-ATT-154")]`.
