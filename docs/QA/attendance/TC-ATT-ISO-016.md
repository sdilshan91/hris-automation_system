---
id: TC-ATT-ISO-016
user_story: US-ATT-011
module: Attendance
priority: critical
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-ISO-016: Multi-tenant isolation — an attendance-policy override cannot be created against another tenant's Location (AC-3)

## 1. Test Objective
Verify the CAL-4b settings CRUD cannot be used to reach across tenants: a Tenant Admin in tenant A supplying tenant B's `locationId` is rejected, nothing is persisted, and no row referencing B's Location exists even when the global query filter is bypassed.

## 2. Related Requirements
- User Story: US-ATT-011 · Acceptance Criteria: AC-3
- NFR: tenant isolation is non-negotiable (Critical Rule #1)

## 3. Preconditions
- Two real tenants, each with its own Location.
- Postgres-backed context.

## 4. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In tenant A's context, `PUT /api/v1/attendance/settings/overrides/{tenantB_locationId}`. | 400 `invalid_location` — B's Location is simply NOT FOUND under A's tenant query filter, the same path as a nonexistent id. No probe oracle: "cross-tenant" and "nonexistent" are indistinguishable to the caller. |
| 2 | Re-read with `IgnoreQueryFilters()`. | **Zero** rows reference B's Location — nothing was persisted. |
| 3 | Resolve policy for a tenant A employee. | Only tenant A's own rows contribute; B's policy never resolves. |

## 5. Notes
The mechanism is the EF global tenant query filter — the service uses **no** `IgnoreQueryFilters` (that appears only in the test, to prove the absence of a leak past the filter). This is the shape the BUG-003 class demands: two real tenant contexts, and an assertion that survives the filter.

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendanceSettingsCrudPostgresTests.CrossTenantLocationId_IsRejected_AndNothingIsPersisted`
- Backing suite trait: `[Trait("TC", "TC-ATT-ISO-016")]`.
