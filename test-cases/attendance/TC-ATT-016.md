---
id: TC-ATT-016
user_story: US-ATT-002
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-016: Overtime detection on clock-out — hours over shift standard are stored as overtime_minutes (AC-3)

## 1. Test Objective
Verify AC-3 / FR-4 / BR-3: when total work minutes exceed the shift's standard hours by more than the tenant's overtime threshold, the excess is classified and stored separately in `overtime_minutes`, the record status is `OVERTIME`, and the summary card shows an overtime badge. Worked example from the story test hints: 10h worked on an 8h shift → `overtime_minutes = 120`.

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2, FR-4
- Business Rules: BR-2, BR-3
- Non-Functional Requirements: NFR-2 (minute accuracy)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- "Day Shift": standard hours = 480 min (8h); overtime threshold = 0 (any excess over standard counts) for this case.
- Auto-break: assume the scenario configures NO break deduction (or the 10h is already net of break) so the worked total is exactly 600 min — document which, to keep the 120-min expectation exact.
- Jordan Lee has ONE open record: `clock_in` 08:00 local, `clock_out` null.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Open record clock_in | 08:00 local | UTC-stored |
| Clock-out time | 18:00 local | 10h raw span = 600 min |
| Break deduction | 0 min (this case) | So net worked = 600 min |
| Shift standard | 480 min (8h) | |
| Expected total_work_minutes | 600 | Net worked |
| Expected overtime_minutes | 120 | 600 - 480 |
| Expected status | OVERTIME | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | While clocked in, click "Clock Out" at 18:00 local | `POST /api/v1/attendance/clock-out` returns 200 OK. |
| 2 | Inspect the response | `total_work_minutes: 600`, `overtime_minutes: 120`, `status: "OVERTIME"`. |
| 3 | Verify the DB row | `total_work_minutes = 600`, `overtime_minutes = 120`, `status = OVERTIME`; the overtime is stored SEPARATELY (not folded into total) so the approval workflow (US-ATT-006) can consume it. |
| 4 | Verify the threshold logic | The 120 min excess is computed as net worked minus shift standard; if the tenant overtime threshold were > 0, only the amount beyond `standard + threshold` would be flagged. |
| 5 | Observe the UI | Summary card shows total "10h 0m" with a blue "Overtime" pill / "+2h 0m OT" badge. |
| 6 | Confirm OT is pending approval | The stored overtime is marked as pending (not auto-approved), consistent with BR-3. |

## 6. Postconditions
- Record closed with `status = OVERTIME` and `overtime_minutes = 120` stored separately and pending approval.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
