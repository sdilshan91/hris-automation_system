---
id: TC-ATT-006
user_story: US-ATT-001
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-006: Grace-period boundary — clock-in at the last grace second is not late; one second past is late (boundary)

## 1. Test Objective
Verify the grace-period boundary defined by BR-4. With a configured grace period (e.g., 15 minutes) after shift start, a clock-in occurring at exactly the last second of the grace window must NOT be flagged late, while a clock-in one second beyond the window must be flagged late. This validates the inclusive/exclusive boundary of the late determination.

## 2. Related Requirements
- User Story: US-ATT-001
- Functional Requirements: FR-1, FR-7
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled, timezone `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Jordan Lee is assigned to "Day Shift" with expected start 09:00 local.
- Tenant grace period = 15 minutes (late threshold therefore at 09:15:00 local).
- The clock allows the clock-in time to be controlled for each sub-case (no pre-existing record before each attempt).

## 4. Test Data
| Sub-case | Local clock-in time | Expected late flag | Notes |
|----------|---------------------|--------------------|-------|
| A (inside, at start) | 09:00:00 | Not late | Exactly on time |
| B (inside, last grace second) | 09:15:00 | Not late | Upper boundary, inclusive |
| C (just outside) | 09:15:01 | Late | One second past grace |
| D (well outside) | 09:45:00 | Late | Clearly late |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: clock in at 09:00:00 local | 201 Created; record is NOT flagged late. |
| 2 | Reset to no-open-record; Sub-case B: clock in at 09:15:00 local | 201 Created; record is NOT flagged late (boundary is inclusive of the grace window). |
| 3 | Reset; Sub-case C: clock in at 09:15:01 local | 201 Created; record IS flagged late. |
| 4 | Reset; Sub-case D: clock in at 09:45:00 local | 201 Created; record IS flagged late. |
| 5 | For each sub-case, verify the late determination uses tenant-local time vs UTC storage | `clock_in` is stored in UTC; the late comparison is computed against the shift start + grace in the tenant timezone. |

## 6. Postconditions
- Each sub-case produces exactly one record with the correct late/not-late classification.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
