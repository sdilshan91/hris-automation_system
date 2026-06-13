---
id: TC-LV-089
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-089: Manager approves a pending leave request -- status Approved, used-ledger entry, balance decreased, audit, notification queued (happy path)

## 1. Test Objective
Verify that an authenticated Manager with `Leave.Approve.Team` permission can approve a pending leave request from a direct report, and that the request transitions to "Approved", a `leave_ledger` entry of type `used` is created (deducting `total_days` from the balance), the running balance decreases, an audit record is written, a `leave-approved` notification is queued to the employee, and the Redis balance cache is invalidated.

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-3, FR-7
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- Manager "Robert Lee" has an active employee record in "acme", is authenticated, and holds `Leave.Approve.Team`.
- Direct report "Jane Smith" (`reports_to_employee_id = Robert`) has a pending Annual Leave request: 2026-07-06..07-08, `total_days = 3`, status `Pending`.
- Jane's current Annual Leave balance is 11.00 days (from the LeaveLedger running total).
- The request has not been cancelled by Jane.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Manager | Robert Lee | Has Leave.Approve.Team |
| Request | Jane Smith, Annual Leave, 2026-07-06..07-08, 3 days | Status Pending |
| Jane balance before | 11.00 days | LeaveLedger running total |
| Comment | "Approved -- enjoy" | Optional (BR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, open Jane's pending request in the detail panel and click "Approve", optionally entering the comment | `POST /api/v1/leaves/{id}/approve` is issued with `X-Tenant-Subdomain: acme` and optional `comment` body; returns 200. |
| 2 | Inspect the request status | `status = Approved`. |
| 3 | Query `leave_ledger` for Jane + Annual Leave | A new row exists with `transaction_type = used`, `days = 3.00`, `leave_request_id` = this request, `balance_after = 8.00` (BR-5: deducted at approval time). |
| 4 | Re-read Jane's current balance | Balance is now 8.00 days (decreased by 3 from 11). |
| 5 | Query the audit log | An audit record exists with `action = Leave.Approved`, `resource_type = LeaveRequest`, and before/after JSON capturing the Pending -> Approved transition (FR-7). |
| 6 | Inspect the notification seam | A `leave-approved` notification for Jane is queued (asynchronously). NOTE: async notification dispatch is the log-only `ILeaveNotificationService` seam (DEFERRED on the notifications module); verify the queue/log call occurs and does not block the API response (NFR-2). |
| 7 | Inspect the cache invalidation | The Redis balance cache for `tenant:{tenantId}:leave_balance:{janeId}:{annualLeaveTypeId}` is invalidated. NOTE: Redis balance cache is DEFERRED module-wide (per vault); balance is read from the LeaveLedger running total, so verify the DB running-total reflects 8.00 now and mark the Redis-invalidation step deferred. |
| 8 | Reload the pending queue | Jane's request has left the pending queue (it is now Approved). |

## 6. Postconditions
- Jane's request is Approved; a `used` ledger entry exists; her balance is 8.00 days.
- Audit record persisted; `leave-approved` notification queued (deferred seam).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
