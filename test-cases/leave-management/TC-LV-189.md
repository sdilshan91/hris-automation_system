---
id: TC-LV-189
user_story: US-LV-010
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-189: Employee cancels a PENDING leave request -- status Cancelled, no ledger entry, manager notification, audit log (happy path)

## 1. Test Objective
Verify that an authenticated Employee can cancel their own pending leave request and that the request transitions to "Cancelled", NO `leave_ledger` entry is created (no balance was ever deducted for a pending request), a `leave-cancelled` notification is queued to the manager, and an audit log entry is written (AC-1, FR-2, FR-5, FR-6).

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-5, FR-6
- Business Rules: BR-5 (reason optional for pending)

## 3. Preconditions
- Tenant "acme" is active (subdomain `acme.yourhrm.com`).
- Employee "Jane Smith" has an active employee record, is authenticated, and reports to manager "Robert Lee".
- Jane has a leave request R: Annual Leave, 2026-08-10..08-12, `total_days = 3`, status `Pending`.
- Jane's Annual Leave balance (LeaveLedger running total) is 11.00 days; no `used` ledger row exists for R (it was never approved).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee | Jane Smith | Requester / acting user |
| Request R | Annual Leave, 2026-08-10..08-12, 3 days | Status Pending |
| Reason | (omitted) | Optional for pending (BR-5) |
| Balance before | 11.00 days | Unchanged by cancellation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane, open R in the "My Leaves" detail view and click "Cancel"; confirm (reason left blank) | `POST /api/v1/leaves/{R}/cancel` is issued with `X-Tenant-Subdomain: acme`; the request succeeds (200) even with no reason (BR-5 -- optional for pending). |
| 2 | Inspect R's status | `status = Cancelled`; `cancelled_at` is set (FR-2). |
| 3 | Query `leave_ledger` for Jane + Annual Leave | NO new ledger row was created for R (FR-2 -- pending was never deducted, so no reversal is needed). |
| 4 | Re-read Jane's balance | Balance remains 11.00 days (no change, as expected). |
| 5 | Inspect the approval-history record | A `leave_approval_history` row exists with `action = Cancelled`, `approver_employee_id = Jane` (self, FR-6). |
| 6 | Inspect the notification seam | A `leave-cancelled` notification for manager Robert is queued. NOTE: async notification dispatch is the log-only `ILeaveNotificationService` seam (DEFERRED on the notifications module); verify the queue/log call occurs and does not block the response. |
| 7 | Query the audit log | An audit record exists capturing the Pending -> Cancelled transition (NFR-4 before/after). |

## 6. Postconditions
- R is Cancelled with no ledger impact; balance unchanged; approval-history + audit recorded; manager notification queued (deferred seam).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
