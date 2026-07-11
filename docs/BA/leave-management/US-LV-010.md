---
id: US-LV-010
module: Leave Management
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-LV-010: Leave Cancellation by Employee

## 1. Description
**As an** Employee,
**I want to** cancel a leave request that I have submitted (pending or approved),
**So that** my leave balance is restored and my manager is informed of the cancellation.

## 2. Preconditions
- Employee is authenticated and has an active employee record.
- A leave request exists with status "Pending" or "Approved" that belongs to the employee.
- The leave period has not yet started (for approved leaves) or is configurable per tenant policy.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee has a pending leave request | They click "Cancel" and confirm | The request status changes to "Cancelled", no balance impact (as no deduction was made for pending), a `leave-cancelled` notification is sent to the manager, and an audit log entry is created |
| AC-2 | Employee has an approved leave request for a future date | They click "Cancel" and provide a cancellation reason | The request status changes to "Cancelled", a reversal ledger entry of type `adjusted` (positive) is created to restore the balance, Redis cache is invalidated, a notification is sent to the manager, and an audit log is created |
| AC-3 | Employee attempts to cancel an approved leave that has already started or passed | They click "Cancel" | The cancellation is blocked with a message: "Cannot cancel leave that has already started. Please contact HR for assistance." (unless tenant policy allows partial cancellation) |
| AC-4 | Employee attempts to cancel a leave that falls within a payroll-locked period | They click "Cancel" | The cancellation is blocked with a message: "Cannot cancel leave for a payroll-locked period. Please contact HR." |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: API endpoint: `POST /api/v1/leaves/{id}/cancel` with required `reason` body.
- FR-2: For pending requests: Status update to "Cancelled"; no ledger entry needed.
- FR-3: For approved requests: Status update to "Cancelled"; create reversal `leave_ledger` entry (type = `adjusted`, positive days) to restore balance.
- FR-4: Redis cache invalidation for `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.
- FR-5: Notification queued to the manager (and next-level approver if multi-level) for both pending and approved cancellations.
- FR-6: Cancellation recorded in `leave_approval_history` with action = "Cancelled" and the employee as the actor.
- FR-7: Tenant-configurable policy: Allow cancellation of approved leaves up to N days before start date (default: 0 = anytime before start).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Cancellation API must respond within 500ms (P95).
- NFR-2: All operations tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-3: Optimistic concurrency using PostgreSQL `xmin` to prevent race conditions (e.g., manager approving while employee cancels).
- NFR-4: Audit log must capture before/after state of the leave request.

## 6. Business Rules
- BR-1: Only the requesting employee can cancel their own leave; managers cannot cancel on behalf (they can reject instead).
- BR-2: Rejected or already-cancelled leaves cannot be cancelled again.
- BR-3: Cancellation of approved leave after the start date is not allowed by default; HR can be contacted to process manually.
- BR-4: If a carry-forward balance was consumed by the cancelled leave, the restoration follows the original allocation logic (carry-forward days restored to carry-forward pool).
- BR-5: Cancellation reason is mandatory for approved leaves; optional for pending leaves.

## 7. Data Requirements
- **Update:** `leave_request.status = 'Cancelled'`, `cancelled_at (timestamptz)`, `cancellation_reason (text)`.
- **Ledger entry (for approved cancellations):** `transaction_type = 'adjusted'`, `days = positive (restoring balance)`, `description = 'Cancellation of leave request {id}'`.
- **Approval history entry:** `action = 'Cancelled'`, `approver_employee_id = self`.

## 8. UI/UX Notes (Notion-like)
- Cancel button visible on the leave request detail view (from "My Leaves" list) for eligible requests.
- Confirmation dialog with cancellation reason text field (required for approved, optional for pending).
- After cancellation: Request card shows "Cancelled" badge in gray with strikethrough styling.
- Toast notification confirms: "Leave request cancelled successfully."
- Cancel button disabled/hidden for ineligible requests (past, locked, rejected) with a tooltip explaining why.
- Mobile: Full-width confirmation dialog with clear action buttons.

## 9. Dependencies
- **US-LV-003**: Leave requests must exist.
- **US-LV-005**: Approved leaves require balance reversal logic (inverse of approval).
- **US-LV-006**: Dashboard must reflect updated balance after cancellation.
- **Notification Service**: For cancellation notifications.
- **Redis**: For balance cache invalidation.

## 10. Assumptions & Constraints
- Partial cancellation (cancelling only some days of a multi-day leave) is not supported in Phase 1; the entire request must be cancelled, and a new one submitted for the revised dates.
- The concurrency mechanism prevents the edge case where a manager approves a request at the same moment the employee cancels it.

## 11. Test Hints
- Test pending cancellation: Cancel a pending request; verify status = "Cancelled", no ledger entry, notification sent.
- Test approved cancellation: Cancel an approved request; verify reversal ledger entry, balance restored, Redis invalidated.
- Test past-date block: Attempt to cancel an approved leave that has already started; verify rejection.
- Test payroll-lock block: Attempt to cancel leave in a locked payroll period; verify rejection.
- Test concurrency: Simulate manager approving while employee cancels simultaneously; verify only one operation succeeds.
- Test tenant isolation: Employee in Tenant A cannot cancel a leave request in Tenant B.
