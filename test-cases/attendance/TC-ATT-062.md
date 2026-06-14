---
id: TC-ATT-062
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-062: working_days defines applicable days and grace_period defines the late threshold (BR-6, BR-4)

## 1. Test Objective
Verify two shift-parameter semantics: (BR-6) `working_days` defines which days of the week the shift applies -- non-working days are not counted for attendance; and (BR-4) `grace_period_minutes` defines the number of minutes after start_time before a clock-in is considered "late". This TC verifies the shift-definition side and the resolver's working-day behavior; the late-flagging consumer is US-ATT-008.

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-2 (working_days, grace_period parameters)
- Business Rules: BR-6 (working_days = applicable days; non-working days not counted), BR-4 (grace_period = late threshold)
- Dependency: US-ATT-008 (late-arrival flagging consumes grace_period)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Shift "Day Shift" start 09:00, grace 10, working_days = [1,2,3,4,5] (Mon-Fri), assigned to E1.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| working_days | [1,2,3,4,5] | Mon-Fri only |
| grace_period_minutes | 10 | Late after 09:10 |
| Saturday | day 6 | Non-working |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET .../employees/E1/shift?date=<a Wednesday>` | Resolves "Day Shift" with the working-day flag = true (Wednesday is in working_days). |
| 2 | `GET .../employees/E1/shift?date=<a Saturday>` | The resolver indicates the date is a NON-working day for E1's shift (day 6 not in working_days), so it is not counted for attendance (BR-6); no expected-hours obligation that day. |
| 3 | Update working_days to include Saturday ([1..6]) and re-resolve | Saturday is now a working day; the change applies from the update per the effective-dating rules. |
| 4 | Verify grace threshold metadata (BR-4) | The resolved shift exposes start_time 09:00 + grace 10 = the 09:10 "late after" threshold that US-ATT-008 consumes; a clock-in at 09:10 is on-time, 09:11 is late. |
| 5 | Boundary -- grace_period = 0 | The shift resolves with a 0-minute grace, so any clock-in strictly after start_time is late (no grace window). |

## 6. Postconditions
- working_days correctly governs applicable/non-working days; the grace-based late threshold is exposed for the late-arrival consumer.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Late-arrival flagging (BR-4) depends on US-ATT-008.** This TC verifies the shift-DEFINITION side -- that the resolved shift exposes the start_time + grace_period late threshold and the working-day applicability. The end-to-end "clock-in flagged late" assertion belongs to US-ATT-008 (and the grace boundary is already exercised against clock-in in TC-ATT-006). **Reported to caller** as a cross-story dependency.
