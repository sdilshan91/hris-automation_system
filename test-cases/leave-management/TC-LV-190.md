---
id: TC-LV-190
user_story: US-LV-010
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-190: Employee cancels an APPROVED future leave with a reason -- status Cancelled, reversal `adjusted` (positive) ledger restores balance, notification, audit (happy path)

## 1. Test Objective
Verify that an authenticated Employee can cancel their own approved, future-dated leave request when a cancellation reason is supplied, and that the request transitions to "Cancelled", a reversal `leave_ledger` entry of type `adjusted` (positive days) is created to restore the previously deducted balance, the Redis balance cache is invalidated, a `leave-cancelled` notification is queued to the manager, and an audit log entry is written (AC-2, FR-1, FR-3, FR-4, FR-5, FR-6).

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-3, FR-4, FR-5, FR-6
- Business Rules: BR-5 (reason mandatory for approved)
- Note: Redis balance cache invalidation (FR-4) is DEFERRED module-wide per docs/vault/modules/leave-management.md; balance is read from the LeaveLedger running total.

## 3. Preconditions
- Tenant "acme" is active.
- Employee "Jane Smith" is authenticated, reports to manager "Robert Lee".
- Jane has an APPROVED Annual Leave request R: 2026-09-14..09-16, `total_days = 3`, status `Approved`, with a `used` ledger entry (`days = 3.00`, `balance_after = 8.00`).
- Today is well before 2026-09-14 (future-dated; AC-3/BR-3 not triggered).
- Jane's current Annual Leave balance (LeaveLedger running total) is 8.00 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Requester / acting user |
| Request R | Annual Leave, 2026-09-14..09-16, 3 days | Status Approved, future |
| Balance before cancel | 8.00 days | After the original `used` deduction |
| Reason | "Trip postponed" | Mandatory for approved (BR-5) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane, open R and click "Cancel"; enter the reason "Trip postponed" and confirm | `POST /api/v1/leaves/{R}/cancel` with body `{ reason: "Trip postponed" }` succeeds (200). |
| 2 | Inspect R's status | `status = Cancelled`; `cancelled_at` set; `cancellation_reason = "Trip postponed"` (FR-2 data). |
| 3 | Query `leave_ledger` for Jane + Annual Leave | A NEW reversal row exists: `transaction_type = adjusted`, `days = +3.00` (positive, restoring), `description = "Cancellation of leave request {R}"`, `balance_after = 11.00` (FR-3). The original `used` -3 row is retained (immutable ledger). |
| 4 | Re-read Jane's current balance | Balance is restored to 11.00 days (8.00 + 3.00 reversal). |
| 5 | Inspect the approval-history record | A `leave_approval_history` row exists with `action = Cancelled`, `approver_employee_id = Jane` (self, FR-6). |
| 6 | Inspect the cache invalidation | The Redis balance cache for `tenant:{tenantId}:leave_balance:{janeId}:{annualLeaveTypeId}` is invalidated. NOTE: Redis is DEFERRED module-wide; verify the DB running-total now reads 11.00 and mark the Redis-invalidation step deferred. |
| 7 | Inspect the notification seam | A `leave-cancelled` notification for Robert is queued (async, non-blocking; deferred seam). |
| 8 | Query the audit log | An audit record exists capturing the Approved -> Cancelled transition with before/after state (NFR-4). |

## 6. Postconditions
- R is Cancelled; an `adjusted` (+3) reversal ledger entry restores the balance to 11.00; approval-history + audit recorded; manager notification queued (deferred seam); Redis invalidation deferred.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
