---
id: TC-ATT-157
user_story: US-ATT-011
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-17
---

# TC-ATT-157: Concurrent first clock-ins create exactly one tenant-default AttendanceSettings row (ISSUE-308 — 23505 race)

## 1. Test Objective
Verify that `AttendancePolicyResolver.GetOrCreateTenantDefaultAsync` is tolerant of a concurrent create race: when several requests for a tenant with **no** `AttendanceSettings` row lazily create the tenant default at the same time, the loser of the `ix_attendance_settings_tenant_location_unique` race must **not** surface a `23505` / 500 — it resolves to the winning row. Regression for ISSUE-308.

## 2. Related Requirements
- User Story: US-ATT-011
- Finding: ISSUE-308 (PR #346)
- Pattern: catch-on-conflict (same as onboarding ISSUE-314)

## 3. Preconditions
- Real PostgreSQL (Testcontainers `postgres:17-alpine`) with migrations applied — the unique index is enforced (InMemory does not enforce it, so this class is Postgres-only).
- A tenant with **no** `AttendanceSettings` row (default not yet lazily created).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Concurrent callers | 8 | each on its own `DbContext` |
| Pre-existing settings rows | 0 | forces the lazy-create path to race |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Fire 8 parallel `GetOrCreateTenantDefaultAsync` calls, each on an independent context. | No call throws (the loser catches `DbUpdateException` / `PostgresException{ SqlState: UniqueViolation }`, detaches, re-reads). |
| 2 | Compare the returned settings ids. | All 8 return the **same** row id (the committed winner). |
| 3 | Count tenant-default rows (`LocationId == null`) for the tenant. | Exactly **1** row exists. |

## 6. Postconditions
- Exactly one tenant-default `AttendanceSettings` row; no orphaned/duplicate rows.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test (concurrency race)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `AttendancePolicyResolverTests.Concurrent_first_clockins_create_exactly_one_tenant_default_issue308` — 8 parallel creates → no throw, single id, exactly one row.
- **Mutation-verified:** neutering the catch filter (`SqlState: "99999"`) makes the arm fail with the loser's `23505`, proving the catch is load-bearing.
- Backing suite trait: `[Trait("TC", "TC-ATT-157")]`.
