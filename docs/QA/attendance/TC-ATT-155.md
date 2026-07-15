---
id: TC-ATT-155
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-155: Tenant attendance-policy CRUD — a Tenant Admin can read and upsert the tenant-default policy (AC-3 admin half)

## 1. Test Objective
Verify US-ATT-011 AC-3's admin half for the **tenant-default** scope: a Tenant Admin can GET and PUT the tenant attendance policy, the upsert is idempotent on a single row, and reading the tenant default is never contaminated by a Location override.

Before CAL-4b, `AttendanceSettings` had **no write path at all** — all 24 policy fields (geofence, IP allowlist, photo, grace period, OT multipliers, caps, thresholds) were created lazily at C# defaults and were never editable, so "a Tenant Admin defines a policy" was unbuildable.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-3 (the admin-configuration half)
- Business Rule: one row per (tenant, location); the row with `LocationId == null` IS the tenant default

## 3. Preconditions
- Caller holds `Attendance.ConfigurePolicy` (already granted to the TenantAdmin / HR bundles).
- Postgres-backed context.

## 4. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `PUT /api/v1/attendance/settings` with a full policy. | 200; the tenant-default row is created with `location_id IS NULL`; every changed field round-trips. |
| 2 | `PUT` the same scope again with different values. | 200; the SAME row is UPDATED — exactly one tenant-default row exists (no second row). |
| 3 | With a Dubai override present, `GET /api/v1/attendance/settings`. | Returns the **tenant** row, never the override. |
| 4 | Call without `Attendance.ConfigurePolicy`. | 403. |

## 5. Notes
`PUT` is a **full replace** of that scope's policy: an omitted JSON field takes the DTO default and therefore RESETS that setting (the known **BUG-117 class**). The FE contract is GET-then-PUT. Tracked as ISSUE-310.

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendanceSettingsCrudPostgresTests.UpsertTenantSettings_CreatesTheTenantDefaultRow_AndRoundTripsEveryField` (step 1)
  - `AttendanceSettingsCrudPostgresTests.UpsertTenantSettings_Twice_UpdatesTheSameRow` (step 2)
  - `AttendanceSettingsCrudPostgresTests.GetTenantSettings_WithOverridesPresent_StillReturnsTheTenantRow` (step 3 — **the invariant arm**; the override is seeded FIRST so a naive unpredicated read is more likely to pick the wrong row. Mutation-verified: reverting the read to an unpredicated `FirstOrDefaultAsync()` reddens this arm, and only this arm.)
- Step 4 (authz) is covered by the `[RequirePermission("Attendance.ConfigurePolicy")]` gate; not separately automated.
- Backing suite trait: `[Trait("TC", "TC-ATT-155")]`.
