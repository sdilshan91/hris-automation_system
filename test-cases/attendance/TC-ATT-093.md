---
id: TC-ATT-093
user_story: US-ATT-007
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-093: Regularized attendance (approved) is treated identically to normal attendance in the summary (BR-7)

## 1. Test Objective
Verify BR-7: an approved regularization that supplies/corrects clock-in/out for a day causes that day to be counted exactly like a normal attendance day -- it contributes to total_present_days and total_work_hours, removes the day from absent/LOP, and (if the corrected hours qualify) contributes to late/overtime counts -- with no penalty for having been regularized.

## 2. Related Requirements
- User Story: US-ATT-007
- Business Rules: BR-1 (present-day definition), BR-7 (regularized = normal)
- Dependencies: US-ATT-003 / US-ATT-004 (approved regularizations)

## 3. Preconditions
- Tenant "acme". HR Officer authenticated with `Attendance.Read.All`.
- Month 2026-05. Employee "Ivy" originally had a missing record on a working day; an APPROVED regularization now supplies clock-in/out meeting the shift minimum for that day.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| regularized day | 1 | approved, supplies clock-in/out |
| corrected hours | meets shift minimum | qualifies as present |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate the 2026-05 summary for Ivy (regularization approved) | The regularized day counts as present (BR-1 met via the corrected times); total_present_days includes it. |
| 2 | Verify it is removed from absent/LOP | The day no longer counts as absent and does not add to lop_days. |
| 3 | Verify work hours | total_work_hours includes the regularized day's hours, accurate to the minute (NFR-5). |
| 4 | Compare with the pre-approval state | Before approval the day was absent/LOP; after approval it is present -- regularization changed the classification (regeneration/refresh picks up the approved correction). |
| 5 | Drill down on Ivy | The day shows the regularized clock-in/out with a regularization indicator, status = present (TC-ATT-085 cross-check). |

## 6. Postconditions
- Approved regularized days are counted as normal present days in all summary aggregates; no regularization penalty is applied.

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
- Only APPROVED regularizations are reflected; pending/rejected ones do not alter the summary. Regularization data integrates with US-ATT-003/004 (implemented). The summary regeneration must pick up newly approved regularizations -- assert the refresh path.
