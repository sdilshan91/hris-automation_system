---
id: TC-ATT-022
user_story: US-ATT-002
module: Attendance
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-022: Clock-out is atomic — a mid-request failure leaves no partial update (NFR-3)

## 1. Test Objective
Verify NFR-3: the clock-out operation (set `clock_out`, compute `total_work_minutes`, set `overtime_minutes`/`status`, update audit, write cache) is atomic. If any part fails mid-request, the record must remain fully OPEN with no partial mutation — never a row with `clock_out` set but `total_work_minutes` null, or status updated without `clock_out`.

## 2. Related Requirements
- User Story: US-ATT-002
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-1, FR-2, FR-4, FR-5

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, with ONE open record (clock_in 09:00 local).
- A fault-injection mechanism is available to fail the transaction after the `clock_out` write but before the derived fields / commit (e.g., forced exception, DB error, or cache-write failure depending on the design).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Open record | clock_in 09:00 local, clock_out null | OPEN |
| Injected fault point | after clock_out set, before commit | Simulate mid-request failure |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inject a fault so the clock-out transaction fails after setting `clock_out` but before computing/committing derived fields | The request fails (5xx or a handled error); the transaction rolls back. |
| 2 | Verify the DB row | The record is STILL OPEN: `clock_out` null, `total_work_minutes` null, `status` unchanged, `overtime_minutes` null. No partial values persisted. |
| 3 | Verify the cache | The status cache still shows "clocked in" (or is not updated to clocked-out); cache and DB stay consistent. (If Redis is not wired, verify the DB-derived dashboard status remains "clocked in".) |
| 4 | Retry the clock-out without the fault | The retry succeeds and produces a single, fully consistent completed record (clock_out + total + status all set together). |
| 5 | Confirm no duplicate side effects | The failed-then-retried sequence yields exactly one completed record; no orphaned overtime/anomaly artifacts from the failed attempt. |

## 6. Postconditions
- On failure the record stays fully open; on retry it completes consistently. No partial/half-written state ever persists.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
