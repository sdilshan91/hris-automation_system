---
id: TC-CHR-328
user_story: US-CHR-013
module: Core HR
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-CHR-328: WorkArrangement=Remote is geofence-exempt at clock-in; OnSite outside geofence is blocked (AC-2 happy path)

## 1. Test Objective
Verify US-CHR-013 AC-2 / FR-4 / FR-5 / BR-4: a `Remote` employee can clock in from **outside** the tenant/location geofence, while an `OnSite` (and `Hybrid`) employee attempting to clock in outside the geofence is **blocked**. Feeds US-ATT-001 / US-ATT-011 geofence policy.

## 2. Related Requirements
- User Story: US-CHR-013
- Acceptance Criteria: AC-2
- Functional Requirements: FR-4, FR-5
- Business Rule: BR-4 (only Remote is exempt; Hybrid stays enforced)
- Cross-reference: US-ATT-001 (geofenced clock-in), US-ATT-011 FR-4 (location geofence)

## 3. Preconditions
- A geofence configured (tenant coordinate/radius, or location override).
- Three employees: `WorkArrangement = Remote`, `= OnSite`, `= Hybrid`.
- Clock-in requests carry coordinates **outside** the geofence radius.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Remote employee | WorkArrangement = Remote (2) | exempt |
| OnSite employee | WorkArrangement = OnSite (0) | enforced |
| Hybrid employee | WorkArrangement = Hybrid (1) | enforced |
| Clock-in location | outside geofence | triggers the gate |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Remote employee clocks in from outside the geofence. | Success (200) — geofence check skipped for Remote. |
| 2 | OnSite employee clocks in from outside the geofence. | Blocked (rejected / out-of-geofence error). |
| 3 | Hybrid employee clocks in from outside the geofence. | Blocked (Hybrid is NOT exempt, BR-4). |
| 4 | OnSite employee clocks in from **inside** the geofence. | Success — enforcement only blocks outside. |

## 6. Postconditions
- Remote workers can record attendance; on-site/hybrid geofence enforcement is unchanged.

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
  - `RemoteClockInTests.RemoteEmployee_OutsideTheGeoFence_CanClockIn` (the exemption)
  - `RemoteClockInTests.OnSiteEmployee_OutsideTheGeoFence_IsStillBlocked` (**the control** — OnSite is the default and every existing employee)
  - `RemoteClockInTests.HybridEmployee_OutsideTheGeoFence_IsStillBlocked` (Hybrid is NOT exempt; mutation-verified — widening the bypass to `!= OnSite` reddens this arm and only this arm)
  - `RemoteClockInTests.RemoteEmployee_IsStillSubjectToTheIpAllowlist` + `..._IsStillSubjectToRequireGeolocation` (**the exemption is the geo-fence RADIUS only** — it must not widen into "Remote skips attendance policy")
  - `RemoteClockInTests.EmployeeWrittenWithoutAnArrangement_DefaultsToOnSite_AndIsStillFenced` (migration safety on live rows)
- Backing suite trait: `[Trait("TC", "TC-CHR-328")]`.
