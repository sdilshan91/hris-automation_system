---
id: TC-ATT-150
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-150: Location-scoped attendance-policy override applies to that Location's employees only (AC-3 happy path)

## 1. Test Objective
Verify US-ATT-011 AC-3 / FR-4: a location-scoped `AttendanceSettings` override for Dubai (e.g. `WeekendOvertimeMultiplier = 3.0`) applies to a **Dubai** employee, while a **Colombo** employee (no location override) still resolves the **tenant default** multiplier (e.g. 2.0). Proves the override layer resolves by the four-tier precedence (employee's Location override → tenant default).

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-3
- Functional Requirement: FR-4
- Business Rule: BR-5 (null-LocationId row = tenant default)

## 3. Preconditions
- Tenant `AttendanceSettings` default row (`LocationId = null`) with `WeekendOvertimeMultiplier = 2.0`.
- Dubai Location with an override row (`LocationId = Dubai`) `WeekendOvertimeMultiplier = 3.0`.
- One Dubai employee, one Colombo employee (Colombo has no override row).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant default weekend OT mult | 2.0 | null-LocationId row |
| Dubai override weekend OT mult | 3.0 | Dubai LocationId row |
| Colombo employee | no override | inherits tenant default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve effective `AttendanceSettings` for the Dubai employee. | `WeekendOvertimeMultiplier == 3.0` (Location override). |
| 2 | Resolve effective `AttendanceSettings` for the Colombo employee. | `WeekendOvertimeMultiplier == 2.0` (tenant default; no override exists). |
| 3 | Assert the tier that supplied each result. | Dubai = Location tier; Colombo = Tenant tier (NFR-3 auditable). |

## 6. Postconditions
- No state change; override is scoped to Dubai employees, tenant default unchanged for everyone else.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendancePolicyResolverTests.LocationOverride_AppliesToThatLocationsEmployees_OthersGetTheTenantDefault` (Dubai override 3.0× for a Dubai employee; Colombo and no-location employees get the tenant 2.0× — three outcomes one shared row could not produce)
  - `AttendancePolicyResolverTests.TenantDefaultAccessor_NeverReturnsALocationOverride` (**the invariant-break regression** — overrides seeded FIRST so a naive read is more likely to pick the wrong row)
  - `AttendancePolicyResolverTests.LazyCreate_CreatesTheTenantDefaultRow_NeverALocationOverride`
  - `LeaveReportServiceTests.Absenteeism_UsesTheTenantDefaultThreshold_NotALocationOverride` (the BR-4 report stays on the tenant default; seed order is load-bearing)
- **Mutation-tested:** reintroducing the pre-CAL-4 unpredicated `FirstOrDefaultAsync()` reddens 3 resolver arms + the absenteeism arm.
- Backing suite trait: `[Trait("TC", "TC-ATT-150")]` on `AttendancePolicyResolverTests`.
