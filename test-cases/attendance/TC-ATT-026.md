---
id: TC-ATT-026
user_story: US-ATT-003
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-026: Submit a regularization for a date with clock-in but no clock-out (MISSED_CLOCK_OUT) links to the existing attendance_log (happy path)

## 1. Test Objective
Verify that when an employee clocked in but forgot to clock out on a recent past working day, submitting a MISSED_CLOCK_OUT regularization creates an `attendance_regularization` row with `status = PENDING` that is LINKED to the existing open `attendance_log` (via `attendance_log_id`), carries only the missing `requested_clock_out`, leaves the original `attendance_log` UNCHANGED at submission time (the record is updated only upon approval per BR-5), and initiates the approval workflow.

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2, FR-3
- Business Rules: BR-5
- Data Requirements: S7 (attendance_log_id FK, nullable)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`, regularization workflow configured, lookback = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`, has a line manager.
- On the target date (2 days ago, a working day), Jordan Lee has ONE existing `attendance_log` with `clock_in` set (09:02 local) and `clock_out` null (forgot to clock out). The UUID of this record is known.
- No pending regularization exists for the target date.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| date | today - 2 days (working day) | Within lookback |
| Existing attendance_log | clock_in 09:02 local, clock_out null | Open record to link |
| regularization_type | MISSED_CLOCK_OUT | Only clock-out missing |
| requested_clock_out | 18:00 local | Required for MISSED_CLOCK_OUT |
| requested_clock_in | (omitted) | Not required for this type |
| reason | "Left for a client site and forgot to clock out." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the regularization drawer for the target date | Drawer pre-populates the date and shows the existing attendance context: clock-in 09:02, clock-out "Missing". |
| 2 | Select type MISSED_CLOCK_OUT, enter clock-out 18:00 and the reason; submit `POST /api/v1/attendance/regularizations` | Response status 201 Created. |
| 3 | Inspect the response body | `regularization_type: "MISSED_CLOCK_OUT"`, `attendance_log_id` = the existing open record's UUID (NOT null), `requested_clock_out` as UTC timestamptz, `requested_clock_in` null, `status: "PENDING"`, non-null `workflow_instance_id`. |
| 4 | Verify the `attendance_regularization` row | One PENDING row whose `attendance_log_id` references the existing open `attendance_log`; `tenant_id` = acme; audit fields set. |
| 5 | Verify the linked `attendance_log` is UNCHANGED | The existing record still has `clock_out` null and the original `clock_in` 09:02 (per BR-5, the log is updated only upon approval in US-ATT-004). |
| 6 | Observe the UI | A "Pending" status pill appears next to the target date; the existing clock-in remains visible for context. |

## 6. Postconditions
- One PENDING MISSED_CLOCK_OUT regularization linked to the existing open `attendance_log`.
- The linked `attendance_log` remains open and unmodified until approval.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
