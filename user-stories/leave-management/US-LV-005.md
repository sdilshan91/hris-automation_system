---
id: US-LV-005
module: Leave Management
priority: Must Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-005: Manager Approves or Rejects Leave Request

## 1. Description
**As a** Manager,
**I want to** approve or reject a pending leave request from my team with an optional comment,
**So that** the employee is notified of the decision and their leave balance is updated accurately.

## 2. Preconditions
- Manager is authenticated and has `Leave.Approve.Team` permission.
- A pending leave request exists from one of the manager's direct reports.
- The leave request has not been cancelled by the employee.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Manager views a pending leave request | They click "Approve" and optionally add a comment | The request status changes to "Approved", a leave ledger entry of type "used" is created to deduct the balance, an audit record is written, a `leave-approved` notification is queued to the employee, and the Redis balance cache is invalidated |
| AC-2 | Manager views a pending leave request | They click "Reject", enter a mandatory rejection reason | The request status changes to "Rejected", no balance deduction occurs, an audit record is written, and a `leave-rejected` notification with the reason is queued to the employee |
| AC-3 | Manager attempts to approve a request but the employee's balance has become insufficient since submission (e.g., another request was approved in the interim) | They click "Approve" | The system warns the manager about insufficient balance and asks for confirmation (if negative balance is allowed for this leave type) or blocks approval (if not allowed) |
| AC-4 | Multi-level approval is configured and the manager is not the final approver | They approve the request | The status changes to "Pending L2 Approval" (or next level), and a notification is sent to the next-level approver |
| AC-5 | Two managers attempt to approve/reject the same request simultaneously | Both submit their decisions | Only the first action succeeds; the second receives a concurrency conflict error ("This request has already been actioned") using PostgreSQL xmin optimistic concurrency |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: API endpoint: `POST /api/v1/leaves/{id}/approve` with optional `comment` body.
- FR-2: API endpoint: `POST /api/v1/leaves/{id}/reject` with required `reason` body.
- FR-3: On approval: Insert `leave_ledger` entry (type = 'used', days = total_days); invalidate Redis cache for `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.
- FR-4: On rejection: No ledger entry; only status update and audit log.
- FR-5: Multi-level approval: Support configurable approval chain (1-3 levels) per tenant; track approval history in `leave_approval_history` table.
- FR-6: Optimistic concurrency using PostgreSQL `xmin` system column via EF Core `UseXminAsConcurrencyToken()`.
- FR-7: Audit log entry with action = `Leave.Approved` or `Leave.Rejected`, resource_type = `LeaveRequest`, before/after JSON.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Approve/Reject API must respond within 500ms (P95).
- NFR-2: Notification queuing must be asynchronous and not block the API response.
- NFR-3: All operations tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-4: Concurrency handling must prevent double-approval or approve-then-reject race conditions.

## 6. Business Rules
- BR-1: Only the designated approver (manager or current-level approver in multi-level workflow) can approve/reject.
- BR-2: Rejection reason is mandatory; approval comment is optional.
- BR-3: A rejected request cannot be re-approved; the employee must submit a new request.
- BR-4: If the request spans a period that has since been locked for payroll, approval is blocked with a message: "Cannot approve leave for a payroll-locked period."
- BR-5: Approval deducts from the balance of the leave type at the time of approval, not at the time of request.

## 7. Data Requirements
- **Table:** `leave_approval_history`
- **Key columns:** `approval_id (uuid PK)`, `tenant_id (uuid FK)`, `leave_request_id (uuid FK)`, `approver_employee_id (uuid FK)`, `approval_level (int)`, `action (varchar(20))` [Approved, Rejected], `comment (text)`, `actioned_at (timestamptz)`, audit columns.
- **Table:** `leave_ledger` (transaction entry on approval)
- **Key columns:** `ledger_id (uuid PK)`, `tenant_id (uuid FK)`, `employee_id (uuid FK)`, `leave_type_id (uuid FK)`, `leave_request_id (uuid FK, nullable)`, `transaction_type (varchar(20))` [accrual, used, adjusted, encashed, carry_forward, expired], `days (numeric(5,2))`, `balance_after (numeric(5,2))`, `transaction_date (date)`, `description (text)`, audit columns.

## 8. UI/UX Notes (Notion-like)
- Approve/Reject buttons prominently displayed in the request detail panel.
- Approve: Green button with checkmark icon; optional comment textarea expands on click.
- Reject: Red outlined button with X icon; mandatory reason textarea appears on click.
- Confirmation modal for approval when balance is insufficient (if negative balance allowed).
- After action: Smooth transition — request slides out of pending queue with a subtle animation.
- Toast notification confirms the action: "Leave request approved for [Employee Name]".
- Mobile: Full-width action buttons at the bottom of the detail view.

## 9. Dependencies
- **US-LV-004**: Manager must be able to view the pending queue.
- **US-LV-003**: Leave requests must exist.
- **US-LV-002**: Leave balances must be tracked in the ledger.
- **Notification Service**: For queuing approval/rejection notifications.
- **Redis**: For balance cache invalidation.
- **Audit Service**: For writing audit log entries.

## 10. Assumptions & Constraints
- Multi-level approval supports a maximum of 3 levels in Phase 1.
- The approval workflow configuration is set at the tenant level (not per leave type in Phase 1).
- Concurrency conflicts should be rare but must be handled gracefully.
- Notification delivery is best-effort; approval is committed even if notification queuing fails (with Polly retry on the notification side).

## 11. Test Hints
- Test approval flow: Approve a request, verify status = "Approved", ledger entry created, balance decreased, Redis cache invalidated.
- Test rejection flow: Reject a request with reason, verify status = "Rejected", no ledger entry, reason stored in approval history.
- Test concurrency: Simulate two simultaneous approval requests; verify only one succeeds.
- Test insufficient balance on approval: Approve when balance is 0 and negative not allowed; verify block.
- Test multi-level: Configure 2-level approval; verify first-level approval moves to "Pending L2" and notifies L2 approver.
- Test payroll lock: Attempt to approve leave for a locked payroll period; verify rejection.
- Test tenant isolation: Manager in Tenant A cannot approve requests in Tenant B.
