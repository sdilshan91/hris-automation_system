---
id: TC-ATT-156
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-156: Location attendance-policy override CRUD — create, update, delete, and fall back to the tenant default (AC-3 admin half)

## 1. Test Objective
Verify US-ATT-011 AC-3's admin half for the **per-Location** scope: a Tenant Admin can create, update and delete a Location's policy override; the override never mutates the tenant default; deleting it returns that Location's employees to the tenant default; and an override may only reference an active, same-tenant Location.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-3
- Business Rule: at most ONE override per (tenant, location); resolution is ROW-LEVEL — the override IS that location's complete policy and does not merge field-by-field with the tenant row

## 3. Preconditions
- Caller holds `Attendance.ConfigurePolicy`.
- A tenant-default policy row exists; Locations "Dubai" (active) and one inactive Location exist.
- Postgres-backed context.

## 4. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `PUT /api/v1/attendance/settings/overrides/{dubaiId}`. | 200; a row with `location_id = dubaiId` is created; the **tenant-default row is untouched**. |
| 2 | Resolve policy for a Dubai employee, then for a Colombo employee. | Dubai gets the override; Colombo gets the tenant default. |
| 3 | `PUT` the same override again. | 200; the SAME row is UPDATED — one row per (tenant, location). |
| 4 | `PUT` an override for an **inactive** Location. | 400 `invalid_location`; nothing persisted. |
| 5 | `DELETE /api/v1/attendance/settings/overrides/{dubaiId}`. | 200; that Location's employees fall back to the tenant default via `AttendancePolicyResolver`. |
| 6 | `GET /api/v1/attendance/settings/overrides`. | Lists every Location override with its `LocationId` + `LocationName`; the tenant-default row is excluded. |

## 5. Notes
DELETE is a **soft** delete (`IsDeleted = true`); both unique indexes are filtered `is_deleted = false`, so a fresh override can be created afterwards without colliding. A concurrent duplicate insert is translated to **409** (`override_already_exists`) rather than surfacing Postgres 23505 as a 500.

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendanceSettingsCrudPostgresTests.UpsertOverride_ScopesToThatLocation_LeavesTheTenantDefaultIntact` (steps 1–2)
  - `AttendanceSettingsCrudPostgresTests.UpsertOverride_Twice_UpdatesTheSameRow` (step 3)
  - `AttendanceSettingsCrudPostgresTests.UpsertOverride_ForAnInactiveLocation_IsRejected` (step 4)
  - `AttendanceSettingsCrudPostgresTests.DeleteOverride_MakesThatLocationsEmployeesFallBackToTheTenantDefault` (step 5)
- The unique-index contract behind step 3 is proven independently by `AttendancePolicyResolverTests` (TC-ATT-151).
- Backing suite trait: `[Trait("TC", "TC-ATT-156")]`.
