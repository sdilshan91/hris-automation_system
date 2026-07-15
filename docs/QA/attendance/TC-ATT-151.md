---
id: TC-ATT-151
user_story: US-ATT-011
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-15
---

# TC-ATT-151: At most one AttendanceSettings override per (tenant, location) — second override rejected (AC-3 negative / boundary)

## 1. Test Objective
Verify US-ATT-011 AC-3 / BR-5 and spec §7.1: creating a **second** `AttendanceSettings` override row for the same `(tenant_id, location_id)` is rejected (unique constraint where not deleted), so a Location's policy is unambiguous.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-3
- Business Rule: BR-5
- Spec §7.1: only one override row per (tenant, location)

## 3. Preconditions
- A Dubai Location already has one `AttendanceSettings` override row.
- Postgres-backed context (asserts the real partial-unique index `(tenant_id, location_id) WHERE is_deleted = false`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing override | Dubai LocationId | one row present |
| Second override attempt | Dubai LocationId | duplicate (tenant, location) |
| OT multiplier | < 1.0 | also assert `>= 1.0` guard |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to create a second override row for the same Dubai LocationId. | Rejected (409/400 or unique-violation surfaced as validation); no second row persists. |
| 2 | Query the override rows for Dubai. | Exactly **one** non-deleted override row remains. |
| 3 | Attempt to save an override with `WeekendOvertimeMultiplier = 0.5`. | Rejected — multipliers must be `>= 1.0` (spec §7.1). |
| 4 | Soft-delete the existing override, then create a new one. | Allowed — the partial index only counts non-deleted rows. |

## 6. Postconditions
- The one-override-per-location invariant holds; multiplier bounds enforced.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendancePolicyResolverTests.SecondOverrideForTheSameLocation_IsRejectedByTheUniqueIndex` (Postgres 23505 on `ix_attendance_settings_tenant_location_unique`)
  - `AttendancePolicyResolverTests.SecondTenantDefaultRow_IsRejected_DespitePostgresNullDistinctness` (⚠ Postgres treats NULLs as DISTINCT, so `(tenant_id, location_id)` alone would permit TWO tenant-default rows — `ix_attendance_settings_tenant_default_unique` (partial, `location_id IS NULL`) is what keeps the tenant default singular. Only a real provider proves this.)
- Backing suite trait: `[Trait("TC", "TC-ATT-151")]` on `AttendancePolicyResolverTests`.
