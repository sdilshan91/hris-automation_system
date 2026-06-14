---
id: TC-ATT-007
user_story: US-ATT-001
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-007: Geo-fence radius edge — coordinates on the boundary are accepted, just outside are rejected (boundary)

## 1. Test Objective
Verify the geo-fence radius boundary defined by FR-3. When `geo_fence_enabled = true`, a clock-in with coordinates exactly at the configured radius from an allowed location must be accepted, while coordinates just beyond the radius must be rejected. Validates the edge of the distance comparison.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Tenant attendance settings: `require_geolocation = true`, `geo_fence_enabled = true`.
- An allowed location is configured: center `40.7128, -74.0060` (NYC office), radius `100` meters.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, no clock-in for the current local day, browser location permission granted.

## 4. Test Data
| Sub-case | Latitude, Longitude | Distance from center | Expected | Notes |
|----------|---------------------|----------------------|----------|-------|
| A (inside) | 40.71285, -74.00600 | ~6 m | Accepted | Well inside |
| B (on boundary) | computed point ~100 m due north | ~100 m | Accepted | Radius is inclusive |
| C (just outside) | computed point ~101 m due north | ~101 m | Rejected | Outside fence |
| D (far) | 40.7306, -73.9352 | ~6 km | Rejected | Clearly outside |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: clock in with coordinates ~6 m from center | 201 Created; record stored with the supplied coordinates. |
| 2 | Reset; Sub-case B: clock in with coordinates ~100 m from center (on the radius) | 201 Created (radius boundary is inclusive). |
| 3 | Reset; Sub-case C: clock in with coordinates ~101 m from center | Rejected (400/422) with a message indicating the location is outside the allowed area; no record created. |
| 4 | Reset; Sub-case D: clock in from ~6 km away | Rejected; no record created. |
| 5 | Verify multiple allowed locations | If two allowed locations are configured, coordinates within ANY allowed location's radius are accepted (FR-3 "any allowed location"). |

## 6. Postconditions
- Accepted sub-cases create exactly one record each; rejected sub-cases create none.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
