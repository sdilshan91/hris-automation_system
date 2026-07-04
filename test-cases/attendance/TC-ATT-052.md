---
id: TC-ATT-052
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: fail
created: 2026-06-14
defect: BUG-048
automated_by:
  - HRM.Tests.Integration.ShiftNameTrimDuplicatePostgresTests.CreateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500
  - HRM.Tests.Integration.ShiftNameTrimDuplicatePostgresTests.UpdateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500
  - HRM.Tests.Integration.ShiftNameTrimDuplicatePostgresTests.CreateShift_DistinctTrimmedName_StillCreates
---

# TC-ATT-052: Duplicate shift name (incl. whitespace-trimmed variants) within the same tenant is rejected cleanly; the same name is allowed in a different tenant (negative + tenant-scoped uniqueness)

## 1. Test Objective
Verify the per-tenant name-uniqueness constraint on `shift` (AC-1, FR-2, Data: name unique per tenant): creating/renaming a shift to a name that already exists in the tenant is rejected with a **clean 409 `duplicate_name`** — including the case where the incoming name differs only by leading/trailing whitespace, which the service **stores trimmed** and must therefore **check trimmed** (BUG-048). The identical name remains valid in a different tenant (uniqueness is scoped by `tenant_id`, not global).

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2 (name parameter)
- Data: `shift.name` unique per tenant (partial unique index `ix_shift_tenant_name_unique` on `(tenant_id, name) WHERE is_deleted = false`)
- Defect guarded: **BUG-048** (HIGH) — `ShiftService.CreateAsync`/`UpdateAsync` checked the RAW request name (`AnyAsync(s => s.Name == request.Name)`) but persisted `request.Name.Trim()`, so a whitespace variant slipped the app-level guard and violated the unique index → **HTTP 500** (`DbUpdateException` / Postgres `23505`) instead of a clean 409.

## 3. Preconditions
- Tenants "acme" and "globex" `active`, Attendance module enabled.
- HR Officers authenticated in each with `Attendance.Shift.Manage`.
- A shift "Day Shift" already exists in acme (e.g. created by TC-ATT-051).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing name (acme) | "Day Shift" | Already taken |
| Exact duplicate (acme) | "Day Shift" | Rejected 409 duplicate_name |
| Whitespace duplicate (acme) | "Day Shift " (trailing space) | **BUG-048** — must reject 409, NOT 500 |
| Rename collision (acme) | "Night Shift" → "Day Shift " | **BUG-048** — update path, must reject 409, NOT 500 |
| Distinct trimmed name (acme) | "  Evening Shift " | Positive control — trims to a non-colliding name → 201 |
| Same name in globex | "Day Shift" | Should be allowed (per-tenant scope) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, `POST /api/v1/attendance/shifts` with name "Day Shift" again | Response 409 with `duplicate_name`; no second acme row is created. |
| 2 | As acme HR, `POST .../shifts` with name **"Day Shift "** (trailing space) | Response **409 `duplicate_name`** (the trimmed name collides). **NOT 500.** Exactly one live "Day Shift" remains — no ghost row from a failed insert. (BUG-048 create trigger.) |
| 3 | With "Day Shift" + "Night Shift" present, `PUT .../shifts/{nightId}` renaming to **"Day Shift "** | Response **409 `duplicate_name`**. **NOT 500.** "Night Shift" is untouched; still exactly one "Day Shift". (BUG-048 update trigger.) |
| 4 | As acme HR, `POST .../shifts` with name "  Evening Shift " | Response 201; stored name is the trimmed "Evening Shift" (positive control — a genuinely distinct trimmed name still creates). |
| 5 | Verify the acme `shift` table | Exactly one shift named "Day Shift"; one "Evening Shift"; no whitespace-variant duplicates. |
| 6 | As globex HR, `POST .../shifts` with name "Day Shift" | Response 201; globex now has its own "Day Shift" — uniqueness is per tenant. |

## 6. Postconditions
- acme has exactly one "Day Shift" and one "Evening Shift"; no 500s were raised and no orphaned/ghost rows exist; globex has its own independent "Day Shift"; no cross-tenant collision.

## 7. Automated Coverage
Bound to the Testcontainers/Postgres integration suite (`HRM.Tests/Integration/ShiftNameTrimDuplicatePostgresTests.cs`) — the EF Core InMemory provider does **not** enforce unique indexes, so the pre-fix 500 only reproduces on real Postgres:
- `CreateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500` — Step 2.
- `UpdateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500` — Step 3.
- `CreateShift_DistinctTrimmedName_StillCreates` — Step 4 (positive control).

Pre-fix these fail (SaveChanges throws `DbUpdateException`/`23505`). Post-fix they pass (clean 409). Steps 1 and 6 (exact duplicate, per-tenant scope) are covered by the API-layer suite.

## 8. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
