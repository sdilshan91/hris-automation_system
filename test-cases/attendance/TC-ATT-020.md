---
id: TC-ATT-020
user_story: US-ATT-002
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-020: Geolocation captured on clock-out when tenant policy requires it (AC-5 / FR-6)

## 1. Test Objective
Verify AC-5 / FR-6: when the tenant policy requires geolocation on clock-out, the browser captures coordinates (if permitted) and the server stores them in `clock_out_latitude` / `clock_out_longitude`; and that the permission-denied path is handled per policy (clock-out proceeds without coordinates when geo is optional, or is blocked/flagged when geo is strictly required — document which the tenant policy is).

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-6
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Tenant policy: `require_geolocation_on_clock_out = true`.
- App served over HTTPS (Geolocation API requires a secure context).
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, with ONE open record (clock_in 09:00 local).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| require_geolocation_on_clock_out | true | Tenant policy |
| Granted coordinates | lat 40.7484, lon -73.9857 | decimal(10,7) |
| Permission state (case A) | granted | Coordinates captured |
| Permission state (case B) | denied | Handle per policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Case A: with location permission granted, click "Clock Out" | The browser prompts/uses geolocation; `POST /api/v1/attendance/clock-out` includes `latitude`/`longitude`; response 200 OK. |
| 2 | Verify the DB row (Case A) | `clock_out_latitude = 40.7484000`, `clock_out_longitude = -73.9857000` stored at decimal(10,7) precision; clock-out otherwise completes normally. |
| 3 | Case B: deny the location permission, click "Clock Out" | The system follows the tenant policy: if geo is strictly required it blocks with a clear message (and stores no coordinates); if treated as best-effort it proceeds and stores nulls. Record which behavior the tenant policy specifies. |
| 4 | Verify HTTPS/secure-context requirement | Over plain HTTP the geolocation prompt does not appear; the flow handles the unavailable-API case gracefully (no crash). |
| 5 | Confirm coordinates are not trusted for tenant scope | Geolocation only populates the geo columns; it never influences `tenant_id` resolution (that stays server-derived). |

## 6. Postconditions
- When granted, clock-out coordinates are persisted; permission-denied is handled deterministically per tenant policy.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
