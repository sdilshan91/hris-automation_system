---
id: TC-LV-090
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-090: Manager rejects a pending leave request with a mandatory reason -- status Rejected, no ledger entry, audit, notification with reason, reason stored in approval history (happy path)

## 1. Test Objective
Verify that a Manager can reject a pending leave request with a mandatory rejection reason, and that the request transitions to "Rejected", NO `leave_ledger` entry is created (no balance change), an audit record is written, a `leave-rejected` notification carrying the reason is queued to the employee, and the reason is persisted in `leave_approval_history`.

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4, FR-7
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Direct report "Alan Park" has a pending Sick Leave request: 2026-07-10, `total_days = 1`, status `Pending`.
- Alan's current Sick Leave balance is 6.00 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | Robert Lee | Has Leave.Approve.Team |
| Request | Alan Park, Sick Leave, 2026-07-10, 1 day | Status Pending |
| Alan balance before | 6.00 days | Must remain unchanged |
| Reason | "Team coverage required that day" | Mandatory (BR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, open Alan's request, click "Reject", enter the mandatory reason, and submit | `POST /api/v1/leaves/{id}/reject` is issued with required `reason` body; returns 200. |
| 2 | Inspect the request status | `status = Rejected`. |
| 3 | Query `leave_ledger` for Alan + Sick Leave | NO new `used` entry is created (FR-4); no balance deduction occurs. |
| 4 | Re-read Alan's current balance | Balance is still 6.00 days (unchanged). |
| 5 | Query `leave_approval_history` | A row exists with `action = Rejected`, `approver_employee_id = Robert`, `comment` = the rejection reason, `actioned_at` set. |
| 6 | Query the audit log | An audit record exists with `action = Leave.Rejected`, `resource_type = LeaveRequest`, before/after JSON (FR-7). |
| 7 | Inspect the notification seam | A `leave-rejected` notification carrying the reason is queued to Alan. NOTE: async notification dispatch is the log-only `ILeaveNotificationService` seam (DEFERRED); verify the queue/log call includes the reason and does not block the response. |

## 6. Postconditions
- Alan's request is Rejected; balance unchanged at 6.00 days; no ledger entry.
- Rejection reason persisted in `leave_approval_history`; audit record written.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
