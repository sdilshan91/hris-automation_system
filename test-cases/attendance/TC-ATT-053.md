---
id: TC-ATT-053
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-053: Zero-duration shift (start_time == end_time) is rejected; shift parameter validation (boundary + negative)

## 1. Test Objective
Verify BR-7 and the FR-2 parameter validation: a SINGLE/ROTATING shift whose start_time equals end_time (zero duration) is rejected, and the supporting parameters validate correctly -- break_duration / grace_period are non-negative integers, working_days contains only valid day numbers (1=Mon..7=Sun) with no duplicates. (Night shifts where end < start are valid and are covered separately in TC-ATT-055.)

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-2 (parameter ranges)
- Business Rules: BR-7 (zero-duration shift invalid)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Zero-duration | start 09:00 / end 09:00 | Invalid (BR-7) |
| Negative break | break_duration_minutes -10 | Invalid |
| Bad working_days | [0, 8] | Out of 1..7 range |
| Valid control | start 09:00 / end 17:00, break 30, days [1,2,3] | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST .../shifts` with start_time == end_time (09:00/09:00), type SINGLE | Response 400 with a zero-duration validation error (BR-7); no row created. |
| 2 | `POST .../shifts` with break_duration_minutes = -10 | Response 400; negative break rejected. |
| 3 | `POST .../shifts` with grace_period_minutes = -5 | Response 400; negative grace rejected. |
| 4 | `POST .../shifts` with working_days = [0, 8] | Response 400; day numbers must be within 1..7. |
| 5 | `POST .../shifts` with working_days = [1, 1, 2] (duplicate) | Response 400 (or normalized to a unique set per the documented rule); assert the implemented behavior is deterministic. |
| 6 | `POST .../shifts` with the valid control body | Response 201; the shift is created (positive control proving only the invalid inputs are blocked). |

## 6. Postconditions
- No invalid shift rows persisted; the valid control shift exists.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
