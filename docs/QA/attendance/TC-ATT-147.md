---
id: TC-ATT-147
user_story: US-ATT-011
module: Attendance
priority: critical
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-147: Four-tier resolution — multi-branch Gulf (Sun–Thu) employee resolves Sun as a workday, Fri as weekend (AC-2a)

## 1. Test Objective
Verify US-ATT-011 AC-2 / FR-2: for an employee with **no personal shift** assigned to a Gulf Location whose `DefaultShiftId` is a Sun–Thu shift (`WorkingDays = {7,1,2,3,4}`, ISO 1=Mon..7=Sun), the resolver returns **Sunday as a working day** and **Friday as a non-working day (weekend)** — the Location tier overrides the tenant Mon–Fri default.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-3 (batched, no N+1)
- Business Rule: BR-3 (Employee → Location → Tenant → code precedence)

## 3. Preconditions
- Gulf Location with `DefaultShiftId` = Sun–Thu shift `{7,1,2,3,4}`.
- Employee assigned to the Gulf Location, **no** effective-dated personal `EmployeeShift`.
- Tenant default shift is Mon–Fri `{1,2,3,4,5}`.
- Postgres-backed context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sunday (ISO 7) | in `{7,1,2,3,4}` | expected WORKING day |
| Friday (ISO 5) | not in set | expected WEEKEND |
| Personal shift | none | forces Location tier |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve the working-day set for the Gulf employee as-of a week. | Set == `{7,1,2,3,4}` (from the Location shift, NOT tenant Mon–Fri). |
| 2 | Ask "is Sunday a working day for E?" | TRUE (Sunday is a workday). |
| 3 | Ask "is Friday a working day for E?" | FALSE (Friday is their weekend). |
| 4 | Assert the tier that supplied the result. | Location tier (NFR-3 auditable). |

## 6. Postconditions
- No state change; resolver is the single source of truth and returns the Location shift set.

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
  - `ShiftScheduleResolverLocationTierTests.Resolver_LocationTier_GulfSunThu_SundayIsWorkday_FridayIsNot` (steps 1–3: Sunday IS a workday, Friday is NOT)
  - `ShiftScheduleResolverLocationTierTests.Resolver_PersonalAssignment_BeatsLocationDefault` (BR-3 tier-1 precedence)
  - `ShiftScheduleResolverLocationTierTests.Resolver_ResolvesManyEmployeesAcrossLocations_WithoutNPlusOne` (FR-3 batching)
- Backing suite trait: `[Trait("TC", "TC-ATT-147")]`.
