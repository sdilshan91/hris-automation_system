---
id: TC-ATT-143
user_story: US-ATT-002
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-04
defect:
  - BUG-047
automated_by: HRM.Tests.Integration.ClockInDuplicateConcurrentPostgresTests.ClockIn_DuplicateSameDay_Returns409Not500_BUG047
---

# TC-ATT-143: Concurrent duplicate clock-in returns a clean 409, not an unhandled 500 (BUG-047 regression)

## 1. Test Objective
Verify that when two clock-ins for the same employee race the BR-1 "at most one open record" pre-check (a concurrent request or a double-submit), the loser is rejected with a clean **409 `already_clocked_in`** instead of surfacing an unhandled `DbUpdateException` (Postgres 23505 on `ix_attendance_log_open_unique`) as an HTTP **500**. Regression guard for **BUG-047**.

## 2. Related Requirements
- User Story: US-ATT-002 (clock-in open-record guard; code path in US-ATT-001 `AttendanceService.ClockInAsync`)
- Acceptance Criteria: AC-2 (single open record)
- Business Rule: BR-1 (at most one un-clocked-out record per employee)
- Defect: BUG-047

## 3. Preconditions
- Real PostgreSQL (Testcontainers) with the migrated schema, including the partial unique index `ix_attendance_log_open_unique` (`tenant_id, employee_id WHERE clock_out IS NULL AND is_deleted = false`).
- One ACTIVE employee linked to the acting user (with the required User/Department/JobTitle FK rows).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee status | Active | Eligible to clock in (BR-5) |
| Blocker row | open attendance_log, uncommitted | Holds the open-record unique key under READ COMMITTED |
| Second clock-in | `ClockInData { Source = WEB }` | The racing request under test |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In an uncommitted transaction, insert an open attendance row for the employee (the competing "first" clock-in). | Row held on the unique key; invisible to READ COMMITTED readers. |
| 2 | Invoke `ClockInAsync` on a separate connection; poll `pg_stat_activity` until its INSERT is confirmed WAITING on a Lock. | The BR-1 pre-check passed (blocker unseen) and the INSERT is blocked on the unique key. |
| 3 | Commit the blocker transaction. | The blocker becomes the winning open record; the blocked INSERT wakes and trips 23505. |
| 4 | Await the `ClockInAsync` result. | Returns `IsFailure`, `StatusCode = 409`, `ErrorCode = already_clocked_in`. No exception propagates (pre-fix: `DbUpdateException`/500). |
| 5 | Count open attendance rows for the employee. | Exactly **1** (the blocker's) — the losing insert and its audit row rolled back. |

## 6. Postconditions
- Exactly one open `attendance_log` row for the employee; no orphaned audit row from the failed insert.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test (concurrency race on the unique index)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation
- **Harness:** xUnit + Testcontainers/PostgreSQL (`postgres:17-alpine`). Postgres is REQUIRED — the EF Core InMemory provider enforces neither the partial unique index nor the concurrent-insert race, so the 500 cannot reproduce there. Deterministic race via an uncommitted blocker row + `pg_stat_activity` lock-wait poll (no fixed sleeps).
- **Binding:** `HRM.Tests.Integration.ClockInDuplicateConcurrentPostgresTests` (`ClockIn_DuplicateSameDay_Returns409Not500_BUG047` + `ClockIn_NoContention_Succeeds_BUG047` positive control).
- **Pre-fix:** `ClockInAsync` had no 23505 catch → the blocked INSERT bubbled `DbUpdateException` (500); the test throws and fails. **Post-fix:** the catch translates it to a clean 409.
