---
id: TC-ATT-091
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-091: Leave reconciliation -- a day on approved leave is counted as leave, never as absent (BR-6)

## 1. Test Objective
Verify BR-6: the summary reconciles with leave data so that approved leave days are NOT counted as absent. A scheduled working day with an approved leave record is counted toward total_leave_days, increments neither total_absent_days nor lop_days, and is shown as "leave" in the drill-down.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-3 (total_leave_days)
- Business Rules: BR-2 (absent = no record AND no approved leave), BR-6 (leave reconciliation)
- Dependencies: Leave Management module (leave data)

## 3. Preconditions
- Tenant "acme". HR Officer authenticated with `Attendance.Read.All`.
- Month 2026-05. Employee "Gita" has 2 approved leave days (no clock-in those days) and is otherwise present; the Leave module records are up to date.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| Gita approved leave days | 2 | no clock-in |
| expected absent | 0 | leave reconciled |
| expected leave_days | 2 | counted as leave |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate the 2026-05 summary for Gita | total_leave_days = 2; total_absent_days excludes those 2 days. |
| 2 | Verify lop_days | lop_days = 0 for those days (leave coverage prevents LOP, BR-3 + BR-6). |
| 3 | Drill down on Gita | The 2 days show status = leave (with the leave type), not absent. |
| 4 | Add a 3rd working day with NO record and NO leave | That day IS counted absent (BR-2) and adds to LOP, while the 2 leave days remain leave -- confirming only leave-covered days are reconciled. |
| 5 | Pending (not yet approved) leave on a working day with no record | NOT reconciled as leave -- counted absent until the leave is approved (only APPROVED leave reconciles per BR-6). |

## 6. Postconditions
- Approved leave days are counted as leave and excluded from absent/LOP; uncovered or pending-leave days are still absent; the summary reconciles with the leave ledger.

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
- Reconciliation assumes the Leave Management module is operational and leave records are up to date (S10). Seeded approved-leave data is used here; the Leave module is implemented (US-LV-* complete).
- Whether pending leave is treated as "not absent" or "absent until approved" follows the documented rule; this TC asserts APPROVED-only reconciliation and flags if the backend differs. **Reported to caller.**
