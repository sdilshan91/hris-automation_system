---
id: TC-ATT-067
user_story: US-ATT-006
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-067: Overtime auto-detected on clock-out -- 9h on an 8h shift with a 30-min threshold creates a 30-min PENDING overtime record (happy path)

## 1. Test Objective
Verify FR-1/FR-2/AC-1/BR-1/NFR-1: when an employee clocks out and the net work time exceeds `standard_hours + overtime_threshold`, the clock-out transaction itself (no extra API call) creates a single `overtime_record` with the excess minutes, `type=AUTO_DETECTED`, and `status=PENDING`. Worked example: a 9h net day on an 8h standard shift with a 30-minute threshold yields `overtime_minutes = 60` of recognised overtime... see Notes -- the brief's "overtime=30 min" maps to the threshold-net definition; this TC pins the exact arithmetic the backend must confirm.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1 (detect when total_work_minutes > standard + threshold), FR-2 (create overtime_record with employee_id, date, overtime_minutes, type, status)
- Non-Functional: NFR-1 (processed as part of the clock-out transaction -- no additional API call)
- Business Rules: BR-1 (overtime only when total exceeds standard + threshold)

## 3. Preconditions
- Tenant "acme" active, Attendance module enabled; overtime rules configured (threshold 30 min, weekday multiplier 1.5x, daily cap 4h, weekly cap 20h).
- Employee "Asha" authenticated with `Attendance.Clock.Self`, assigned a SINGLE shift with standard_hours = 8h (480 min) on a weekday.
- Asha has an OPEN attendance_log clocked in 9h ago (net of any auto-break), so the clock-out will compute 9h net.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| standard_hours | 480 min | 8h shift standard |
| overtime_threshold | 30 min | tenant-configurable default |
| net_work_minutes at clock-out | 540 min | 9h net |
| day type | weekday | multiplier 1.5x |
| expected overtime_minutes | 60 | 540 - 480 (excess past standard; > 30-min threshold so recognised) |
| expected status | PENDING | AC-1 |
| expected type | AUTO_DETECTED | FR-2 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `POST /api/v1/attendance/clock-out` on the open 9h record | 200; the attendance_log closes with total_work_minutes = 540 and overtime surfaced (see Step 3). No separate overtime API call is made (NFR-1). |
| 2 | `GET /api/v1/attendance/overtime/my` | Exactly one overtime_record for today: `overtime_minutes = 60`, `type = AUTO_DETECTED`, `status = PENDING`, `multiplier = 1.50` (weekday), `approved_minutes = null`, linked to the just-closed attendance_log_id, tenant_id = acme. |
| 3 | Inspect the clock-out response / attendance_log | overtime is reflected consistently with US-ATT-002 (TC-ATT-016 stores overtime_minutes on the log); the new overtime_record is the approval-workflow surface created in the SAME transaction. |
| 4 | Confirm the excess is measured against NET (post auto-break) minutes | Overtime is computed on the net total, not gross (consistent with the attendance vault: overtime computed on the post-break NET). |
| 5 | Re-run clock-out / replay | No second overtime_record is created for the same closed session (one record per detected session). |

## 6. Postconditions
- One PENDING AUTO_DETECTED overtime_record exists for Asha for today, tenant-scoped, awaiting manager approval; the attendance_log is COMPLETE/overtime-flagged.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Overtime arithmetic -- the brief vs the AC.** The task brief phrased this as "9h on 8h shift, 30-min threshold -> overtime=30 min." Two defensible definitions exist: (a) overtime = excess past standard (540-480 = 60 min), with the threshold acting only as a GATE that decides whether any overtime is recognised; (b) overtime = excess past standard+threshold (540-480-30 = 30 min), where the threshold is also subtracted from the counted minutes. FR-1 ("detect overtime when total exceeds standard + threshold") describes the GATE, which favours (a) = 60 min. This TC asserts (a) and flags the ambiguity. **Reported to caller** -- confirm against the backend's `AttendanceCalculator`/overtime detector which definition is implemented and align this TC's expected value (60 vs 30) accordingly; the boundary TC (TC-ATT-068) is unaffected because at 8h20m no overtime is recognised under either definition.
- US-ATT-002 already stores `overtime_minutes` on the attendance_log (TC-ATT-016). US-ATT-006 adds the `overtime_record` approval surface; this TC verifies the record is created in the clock-out transaction (NFR-1), not the legacy log field alone.
