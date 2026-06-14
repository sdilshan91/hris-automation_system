---
id: TC-ATT-072
user_story: US-ATT-006
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-072: Pre-approval policy -- overtime worked without a pre-approval request is flagged UNAPPROVED and excluded from payroll (happy path + negative)

## 1. Test Objective
Verify AC-2/FR-4/BR-6: when the tenant policy requires pre-approval for overtime, an employee must submit an overtime pre-approval request before working overtime; overtime auto-detected at clock-out WITHOUT a matching pre-approval is recorded with `status = UNAPPROVED` and excluded from payroll until HR reviews it. The pre-approved path produces a `type = PRE_APPROVED` record instead.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-2
- Functional Requirements: FR-4 (pre-approval workflow when tenant policy requires it)
- Business Rules: BR-6 (overtime without pre-approval recorded UNAPPROVED, excluded from payroll until HR reviews)

## 3. Preconditions
- Tenant "acme" with overtime pre-approval policy = ON.
- Employee "Asha" authenticated with `Attendance.Clock.Self`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| pre-approval policy | ON | tenant config |
| pre-approval form fields | date, expected overtime hours, reason | §8 simple form |
| net work at clock-out | 9h on 8h shift | 1h overtime |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, work overtime and clock out WITHOUT having submitted a pre-approval | An overtime_record is created with `status = UNAPPROVED` (not PENDING), tenant-scoped. |
| 2 | Verify payroll exclusion of the UNAPPROVED record | The UNAPPROVED record is NOT flagged payroll-ready; the payroll-ready/lop-equivalent feed excludes it until HR reviews (the payroll-ready flag is the US-ATT-009 seam -- see Notes). |
| 3 | As Asha, submit a pre-approval BEFORE the shift: `POST /api/v1/attendance/overtime/pre-approval` { date, expectedHours, reason } | 201; a pre-approval request is created for the date, tenant- and employee-scoped. |
| 4 | Work the overtime and clock out (matching the pre-approval) | The overtime_record is `type = PRE_APPROVED` and follows the normal PENDING->approval path (not UNAPPROVED). |
| 5 | With pre-approval policy OFF (control), clock out with overtime | The record is `AUTO_DETECTED` / `PENDING` (per TC-ATT-067) -- UNAPPROVED only applies when the policy requires pre-approval (BR-6). |
| 6 | HR reviews an UNAPPROVED record | HR can move it into the normal approval flow; until then it stays excluded from payroll (BR-6). |

## 6. Postconditions
- Overtime worked without pre-approval under an ON policy is UNAPPROVED and payroll-excluded; pre-approved overtime flows normally; policy OFF restores the auto-detect PENDING path.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Payroll-ready exclusion (Step 2)** is verified against the overtime record's payroll-ready flag / the report-side classification now; the actual consumption by the payroll engine is US-ATT-009 / the Payroll module, CONDITIONAL on it. The attendance side (UNAPPROVED is not flagged payroll-ready) is verifiable today. **Reported to caller.**
- The "expected overtime hours" on the pre-approval form is the employee's estimate; the ACTUAL overtime is what auto-detection records at clock-out. Whether a large actual-vs-expected variance re-flags the record is not specified by the story -- not asserted here; flag to the BA if needed.
