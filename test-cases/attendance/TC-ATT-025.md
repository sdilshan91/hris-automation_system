---
id: TC-ATT-025
user_story: US-ATT-003
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-025: Submit a regularization for a date with no attendance record (MISSED_BOTH) creates a PENDING request (happy path)

## 1. Test Objective
Verify that an authenticated employee with the `Attendance.Regularize.Self` permission can submit an attendance regularization request for a recent past working day on which they have NO `attendance_log` record at all (forgot both clock-in and clock-out), and that the system creates a new `attendance_regularization` row with `status = PENDING`, `regularization_type = MISSED_BOTH`, `attendance_log_id = null` (no existing record to link), the requested clock-in/clock-out times persisted as UTC `timestamptz`, the reason stored, `tenant_id`/`employee_id` taken from the session context, and a workflow instance initiated for manager approval. No `attendance_log` is created at submission time (it is created only on approval per S10/BR-5).

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-3
- Business Rules: BR-1, BR-5
- Data Requirements: S7 (attendance_regularization record)

## 3. Preconditions
- Tenant "acme" exists, `active`, subdomain `acme.yourhrm.com`, timezone `America/New_York`, Attendance module enabled.
- A regularization approval workflow is configured for the tenant (single-level: line manager).
- Tenant regularization lookback period = 7 days (default).
- Employee "Jordan Lee" is `active`, authenticated (valid JWT with `tenant_id` + `employee_id` claims), holds `Attendance.Regularize.Self`, and has an assigned line manager.
- For the target date (3 days ago, a working day), Jordan Lee has NO `attendance_log` record (open or completed).
- No pending regularization exists for the target date.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | tz America/New_York |
| date | today - 3 days (working day) | Within 7-day lookback |
| regularization_type | MISSED_BOTH | Forgot clock-in and clock-out |
| requested_clock_in | 09:00 local | Required for MISSED_BOTH |
| requested_clock_out | 17:30 local | Required for MISSED_BOTH |
| reason | "Badge reader at the lobby was offline all day." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the attendance history page, locate the target date showing a missing record and click "Request Regularization" | The Notion-style regularization drawer slides in from the right, pre-populated with the selected date; existing attendance data area shows "No record" for that day. |
| 2 | Select type MISSED_BOTH, enter clock-in 09:00, clock-out 17:30, and the reason; submit `POST /api/v1/attendance/regularizations` | Request is sent with `X-Tenant-Subdomain: acme` and the bearer token. Response status is 201 Created. |
| 3 | Inspect the response body | Body contains `regularization_id` (UUID), `tenant_id` = acme's tenant ID, `employee_id` = Jordan Lee, `date` = target date, `regularization_type: "MISSED_BOTH"`, `requested_clock_in`/`requested_clock_out` as UTC timestamptz, `attendance_log_id: null`, `status: "PENDING"`, and a non-null `workflow_instance_id`. |
| 4 | Verify the `attendance_regularization` row in the database | Exactly one row with the returned `regularization_id`; `status = PENDING`; `attendance_log_id` IS NULL; `requested_clock_in`/`requested_clock_out` stored in UTC; `reason` persisted; `tenant_id` = acme; `created_at`/`created_by` audit fields set. |
| 5 | Verify NO attendance_log was created | No `attendance_log` exists for Jordan Lee on the target date (per S10/BR-5, the log is created only upon approval in US-ATT-004). |
| 6 | Observe the UI after submission | A Notion-style status pill "Pending" appears next to the target date in attendance history; the approval chain (line manager) is shown; a non-blocking success toast confirms submission. |

## 6. Postconditions
- One `attendance_regularization` row exists (PENDING, type MISSED_BOTH, attendance_log_id null) for Jordan Lee on the target date.
- A workflow instance for manager approval is initiated.
- No `attendance_log` exists yet for the target date.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
