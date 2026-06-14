---
id: TC-ATT-012
user_story: US-ATT-001
module: Attendance
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-012: Two simultaneous clock-in requests create only one record (concurrency / race condition)

## 1. Test Objective
Verify FR-2, BR-1, and NFR-4: when two clock-in requests for the same employee arrive simultaneously (double-click, double-tap, or duplicate network submission), exactly one `attendance_log` record is created. The second request must be rejected or coalesced (idempotent within the 5-second window), never producing a duplicate open record.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-4
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, and has NO open clock-in for the current local day.
- A test harness capable of firing two requests concurrently (same employee, same JWT) with minimal time skew.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Concurrent requests | 2 | Same employee, near-simultaneous |
| Idempotency window | 5 seconds | NFR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Fire two `POST /api/v1/attendance/clock-in` requests for Jordan Lee at the same instant | Both requests complete. At most ONE returns 201 Created; the other returns 409/200-idempotent (existing record) and does NOT create a second row. |
| 2 | Verify the database | Exactly ONE open `attendance_log` exists for Jordan Lee today. No duplicate. |
| 3 | Verify the mechanism | A DB-level uniqueness/guard (e.g., unique constraint on tenant_id + employee_id + open-status/day, or an atomic upsert/serializable transaction) — not just an application-level pre-check — prevents the race. |
| 4 | Repeat the burst 10 times (each after clean reset) | Every run yields exactly one record; no run produces two. Confirms the guard is not timing-dependent. |
| 5 | Fire a second identical request within 5 seconds of a successful clock-in | The duplicate is treated idempotently (no new record), satisfying NFR-4. |

## 6. Postconditions
- Exactly one open record per employee per day regardless of concurrent submissions.
- No duplicate rows under any repeated burst.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
