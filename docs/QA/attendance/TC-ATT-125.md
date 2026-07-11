---
id: TC-ATT-125
user_story: US-ATT-009
module: Attendance
priority: high
type: functional
status: fail
created: 2026-06-15
---

# TC-ATT-125: Terminated employees included up to last working day; payroll cutoff date determines which attendance days are included in the period

## 1. Test Objective
Verify two period-boundary business rules feeding payroll-data (BR-7, BR-8): a terminated employee's attendance is included only up to their last working day (not beyond), and the tenant payroll cutoff date determines the date window of attendance days included in the current payroll period.

## 2. Related Requirements
- User Story: US-ATT-009
- Business Rules: BR-7 (terminated employees included up to last working day), BR-8 (payroll cutoff date determines included attendance days)
- Dependency: Core HR (employment status / last working day), tenant payroll cutoff config (§10, default last day of month)
- API: GET /api/v1/attendance/payroll-data?month=

## 3. Preconditions
- Tenant "acme"; monthly summary generated for 2026-05.
- Employee "Carl" terminated effective 2026-05-15 (last working day 2026-05-14) per Core HR.
- Tenant cutoff configured to the 25th for one sub-case; default (month end) for another.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Carl last working day | 2026-05-14 | from Core HR |
| Carl post-termination dates | 2026-05-15..31 | must NOT be counted |
| cutoff (case A) | 25th | window = prev-month 26th .. current 25th |
| cutoff (case B) | month end (default) | window = 1st .. 31st |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET payroll-data?month=2026-05&employeeIds=Carl` | Carl's totals (working/present/absent/lop) count days only up to 2026-05-14; dates 2026-05-15..31 contribute nothing (BR-7). |
| 2 | Confirm Carl is NOT marked absent for post-termination working days | No phantom absences/LOP after the last working day. |
| 3 | Case A -- tenant cutoff = 25th; pull the current payroll period | The included attendance window runs from the prev month's 26th to the current 25th (not the calendar month) -- days after the 25th roll to the next period (BR-8). |
| 4 | Case B -- tenant cutoff = month end (default) | The window is the full calendar month 1st..31st. |
| 5 | An active employee spanning the cutoff boundary | Days on/before cutoff are in the current period; days after are excluded from it. |
| 6 | Re-hire / mid-period join (joined 2026-05-10) | Days before the join date are not counted; period start respects employment start. |

## 6. Postconditions
- Terminated employees contribute attendance only through their last working day; the payroll-data window honours the tenant cutoff date. No data beyond employment dates or outside the cutoff window leaks into the period.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- BR-7 depends on Core HR exposing employment status + last working day; if absent, the termination cut-off branch is CONDITIONAL on that integration while the cutoff-window logic (BR-8) is verifiable independently from the tenant config. **Reported to caller.**
- BR-8 cutoff is tenant-configurable (§10, default last day of month). If the cutoff config surface is not yet present, the default month-end window (Case B) is verified unconditionally and the 25th case (Case A) is CONDITIONAL on that config existing. **Reported to caller.**
- The payroll FINALIZE that consumes this window is PAYROLL-MODULE (DEFERRED, see TC-ATT-121/122 BR-1).
