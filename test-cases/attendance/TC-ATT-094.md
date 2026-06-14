---
id: TC-ATT-094
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-094: Present-day and absent-day definitions -- clock-in + meets shift minimum = present; scheduled working day with no record and no leave = absent (BR-1/BR-2)

## 1. Test Objective
Verify BR-1 and BR-2 at the classification boundary: a "present day" requires a clock-in record (manual or regularized) AND total work hours meeting the shift's minimum threshold; an "absent day" is a scheduled working day with no attendance record and no approved leave. Days that clock in but fall below the shift minimum, and non-working days, are classified correctly (not as full present, not as absent).

## 2. Related Requirements
- User Story: US-ATT-007
- Business Rules: BR-1 (present definition), BR-2 (absent definition)
- Functional Requirements: FR-3 (present/absent columns)

## 3. Preconditions
- Tenant "acme". HR Officer authenticated with `Attendance.Read.All`. Shift standard 8h, minimum threshold 4h (half-day policy off for this TC to isolate BR-1/BR-2).
- Employee "Jad": Day A = clock-in + 8h (full); Day B = clock-in + below-minimum (e.g. 2h); Day C = scheduled working day, no record, no leave; Day D = a weekly-off; Day E = scheduled working day with approved leave.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Day A | clock-in, 8h | present |
| Day B | clock-in, 2h (< minimum) | not full present |
| Day C | no record, no leave, working day | absent (BR-2) |
| Day D | weekly-off | not absent |
| Day E | approved leave | leave, not absent |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate the summary for Jad | Day A counts toward total_present_days (BR-1 met: clock-in + >= minimum). |
| 2 | Verify Day B (below minimum) | NOT counted as a full present day -- classified per the short-day rule (e.g. SHORT_DAY from US-ATT-002); does not satisfy BR-1's minimum-hours condition. |
| 3 | Verify Day C | Counted as absent (BR-2: scheduled working day, no record, no leave) and adds to LOP. |
| 4 | Verify Day D (weekly-off) | NOT counted absent (not a scheduled working day, BR-2/BR-4). |
| 5 | Verify Day E (approved leave) | NOT counted absent (leave reconciled, BR-2/BR-6); counted as leave. |
| 6 | Boundary -- clock-in exactly at the shift minimum | Counted as present (>= minimum satisfies BR-1); one minute below is not full present. |

## 6. Postconditions
- Present requires clock-in AND minimum hours; absent requires a scheduled working day with no record and no leave; below-minimum, weekly-off, and leave days are classified outside present/absent accordingly.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- This TC isolates BR-1/BR-2 with half-day policy OFF; the half-day (0.5 present) interaction is covered by TC-ATT-090.
- The shift minimum threshold comes from US-ATT-005 (minimum_hours); seeded shift config is used. Below-minimum classification (short-day) integrates with US-ATT-002 TC-ATT-017.
