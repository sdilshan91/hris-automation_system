---
id: TC-ATT-040
user_story: US-ATT-004
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-040: Approval queue lists all PENDING regularizations for the manager's direct reports with employee, date, requested times, reason, and submission date (happy path)

## 1. Test Objective
Verify AC-3/FR-1: when a manager opens the approval queue, the system returns all PENDING regularization requests for the manager's direct reports (and only those), each row showing employee name, date, requested clock-in/clock-out times, reason, and submission date. Already-decided requests and other managers' teams do not appear; the list is filterable.

## 2. Related Requirements
- User Story: US-ATT-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1 (filterable list of pending requests for the manager's team), FR-7 (scoped to the manager's direct reporting hierarchy)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Dana's direct reports: Jordan Lee and Morgan Vale.
- Jordan has 1 PENDING and 1 already-APPROVED regularization; Morgan has 1 PENDING.
- "Sam Park" reports to a DIFFERENT manager (Lee Chan) and has a PENDING regularization.
- Manager-employee hierarchy comes from Core HR `manager_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | Dana Wells | direct reports: Jordan, Morgan |
| Expected rows | Jordan (PENDING), Morgan (PENDING) | 2 rows |
| Excluded | Jordan's APPROVED, Sam Park's PENDING | decided / not a direct report |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `GET /api/v1/attendance/regularizations/approval-queue` (PENDING filter default) | Response 200; exactly 2 rows -- Jordan's PENDING and Morgan's PENDING. |
| 2 | Inspect each row's fields | Each row shows employee name, regularization date, requested clock-in/clock-out times (in tenant-local display), reason text, and the submission date (created_at). |
| 3 | Confirm exclusions | Jordan's APPROVED request is NOT in the queue (only PENDING); Sam Park's PENDING request is NOT present (not Dana's direct report). |
| 4 | Apply a filter (e.g. by employee = Morgan, or by date range) | The queue narrows to matching PENDING rows only; FR-1 filtering works. |
| 5 | Expand a row | Full request detail (type, both requested times, full reason, submitted-on, current workflow step) is shown without navigating away. |

## 6. Postconditions
- The queue reflects only the manager's direct-report PENDING requests with the required columns; decided and out-of-team requests are excluded.

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
- The pending-approval badge count (UI/UX S8) should equal the queue row count (2 here); badge delivery via the notification/menu surface is DEFERRED on US-NTF, the count derivation from the queue is verifiable now.
