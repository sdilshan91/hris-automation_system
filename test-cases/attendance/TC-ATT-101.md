---
id: TC-ATT-101
user_story: US-ATT-008
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-101: Late clock-in beyond grace -- 09:00 shift, 15-min grace, clock-in 09:20 -> is_late=true, late_minutes=20, late_by=5 (happy path)

## 1. Test Objective
Verify AC-1/FR-1/FR-3/BR-1: when an employee clocks in past the shift start + grace window, the attendance record is marked Late. Worked example: shift starts 09:00, grace 15 min, clock-in 09:20 -> `is_late = true`, `late_minutes = 20` (minutes after shift start), and `late_by = 5` (minutes after the grace cutoff of 09:15). Detection runs inline in the clock-in transaction (NFR-1).

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1 (compare clock-in vs start + grace), FR-3 (persist is_late, late_minutes on attendance_log)
- Non-Functional: NFR-1 (inline detection)
- Business Rules: BR-1 (late = clock_in > start + grace)

## 3. Preconditions
- Tenant "acme" active, Attendance module enabled.
- Employee "Asha" authenticated with `Attendance.Clock.Self`, assigned a SINGLE shift with start_time 09:00, grace_period_minutes = 15, on a working weekday.
- No open clock-in for today.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift start_time | 09:00 | SINGLE shift |
| grace_period_minutes | 15 | grace cutoff = 09:15 |
| clock-in time | 09:20 | past grace |
| expected is_late | true | AC-1 |
| expected late_minutes | 20 | minutes after shift start (09:20 - 09:00) |
| expected late_by | 5 | minutes after grace cutoff (09:20 - 09:15) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `POST /api/v1/attendance/clock-in` at 09:20 (local) | 200; clock-in accepted (lateness does not block the punch -- it is recorded, not rejected). |
| 2 | Inspect the created attendance_log | `is_late = true`, `late_minutes = 20`, `late_by = 5` (per AC-1's distinction: late_minutes measured from start, late_by measured from grace cutoff). Tenant_id = acme. |
| 3 | Inspect the UI daily attendance view | An amber/red "Late by 20 min" badge appears next to the clock-in time (§8). |
| 4 | Confirm inline computation | No second API call is required; is_late/late_minutes are set within the clock-in transaction (NFR-1). |
| 5 | Confirm monthly summary feed | This late increments the employee's monthly late count surfaced in the US-ATT-007 summary (FR-3 of US-ATT-007 depends on this detection). |

## 6. Postconditions
- Asha's attendance_log for today is flagged late (is_late=true, late_minutes=20), tenant-scoped; the late count for the month is incremented.

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
- **late_minutes vs late_by ambiguity.** AC-1 states `late_minutes = 20` AND `late_by = 5` for the same punch -- i.e. two distinct fields: late_minutes measured from shift start (20), late_by measured from the grace cutoff (5). FR-3's data fields list only `late_minutes`. This TC asserts late_minutes = 20 (from start) per FR-3/the data model, and additionally checks late_by = 5 if the backend exposes it. **Reported to caller** -- confirm which definition `late_minutes` carries (from-start vs from-grace) in the `AttendanceCalculator`, and whether `late_by` is a persisted field; align the expected values accordingly.
