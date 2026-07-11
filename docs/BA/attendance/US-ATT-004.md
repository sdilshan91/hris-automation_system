---
id: US-ATT-004
module: Attendance
priority: Must Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-004: Manager Approves/Rejects Regularization Requests

## 1. Description
**As a** Manager,
**I want to** review and approve or reject attendance regularization requests from my team members,
**So that** attendance records are corrected only with proper authorization and accountability.

## 2. Preconditions
- Manager must be authenticated with a valid JWT session.
- Manager must have the `Attendance.Approve.Team` permission.
- One or more pending regularization requests must exist for team members under the manager.
- The Attendance module must be enabled for the tenant.
- The approval workflow for attendance regularization must be configured for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A pending regularization request exists for a team member | Manager clicks "Approve" with an optional comment | The regularization status is updated to "Approved," the `attendance_log` record is created or updated with the regularized times, and the employee is notified |
| AC-2 | A pending regularization request exists for a team member | Manager clicks "Reject" with a mandatory reason | The regularization status is updated to "Rejected," and the employee is notified with the rejection reason |
| AC-3 | Manager views the pending regularization queue | Manager navigates to the approval queue | The system displays all pending regularization requests for the manager's direct reports, showing employee name, date, requested times, reason, and submission date |
| AC-4 | A multi-level approval workflow is configured and the manager is the first approver | Manager approves the request | The workflow advances to the next approver (e.g., HR), and the status remains "Pending" until all levels approve |
| AC-5 | Manager attempts to approve a regularization for an employee not in their team | Manager submits the approval | The system denies the action with: "You are not authorized to approve requests for this employee." |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide a filterable list of pending regularization requests for the manager's team.
- FR-2: On approval, the system shall create or update the corresponding `attendance_log` record with the regularized clock-in/clock-out times and recalculate `total_work_minutes`.
- FR-3: On rejection, the system shall require a reason (minimum 10 characters) and store it in the workflow history.
- FR-4: The system shall advance the workflow instance state according to the tenant's configured approval chain.
- FR-5: The system shall send notifications (in-app + optional email) to the employee upon approval or rejection.
- FR-6: The system shall log the approval/rejection action in the audit log with the manager's user ID, timestamp, and comment.
- FR-7: The system shall enforce that the manager can only approve requests for employees in their direct reporting hierarchy.
- FR-8: The system shall update the Redis cache for the affected employee's daily attendance status upon approval.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The approval queue page must load within 2 seconds at P95 for up to 50 pending requests.
- NFR-2: The approval/rejection action must be atomic: either the regularization status and attendance_log are both updated, or neither is.
- NFR-3: PostgreSQL RLS must enforce tenant isolation; managers can only see requests within their tenant.
- NFR-4: All approval actions must be immutable in the audit log (no deletion or modification).

## 6. Business Rules
- BR-1: Rejection requires a mandatory reason (minimum 10 characters).
- BR-2: Approval comment is optional.
- BR-3: Once a request is approved or rejected, it cannot be changed (immutable decision). A new regularization must be submitted if correction is needed.
- BR-4: If the workflow has multiple levels, the attendance_log is only updated when the final approver approves.
- BR-5: If the regularized date falls within a payroll period that has since been locked, the approval is blocked with a message to contact HR.
- BR-6: Managers cannot approve their own regularization requests; these must route to the manager's own supervisor or HR.
- BR-7: Bulk approval is supported: managers can select multiple requests and approve them with a single action.

## 7. Data Requirements
**Input (approval/rejection):**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| regularization_id | UUID | Yes | The request to act on |
| action | enum | Yes | 'APPROVE' or 'REJECT' |
| comment | text | Conditional | Mandatory for rejection, optional for approval |

**Updated records:**
- `attendance_regularization.status` -> 'APPROVED' or 'REJECTED'
- `attendance_regularization.updated_at`, `updated_by` -> manager info
- `attendance_log` -> created or updated with regularized times (on final approval)
- `workflow_instance` -> step completed, advanced or finalized
- `audit_log` -> new entry for the approval action

## 8. UI/UX Notes (Notion-like)
- Display the approval queue as a Notion-style table/list with columns: Employee, Date, Type, Requested Times, Reason, Submitted On.
- Each row should be expandable to show full details without navigating away.
- Approve/Reject buttons should be inline with each row, with a slide-down comment area on click.
- Support bulk selection with checkboxes and a "Bulk Approve" action button in the toolbar.
- Use Notion-style status pills: amber "Pending," green "Approved," red "Rejected."
- On mobile, use a card-based layout with swipe gestures for approve/reject (optional enhancement).
- Show a badge count on the navigation menu for pending approvals.
- After action, the row should animate out of the pending list with a smooth transition.

## 9. Dependencies
- US-ATT-003: Regularization requests must exist before they can be approved/rejected.
- Approval Workflow Engine (technical document S34): Drives the multi-level approval flow.
- Notification System: Sends notifications to employees on approval/rejection.
- Core HR module: Manager-employee reporting hierarchy for authorization.
- US-ATT-009: Approved regularizations affect payroll calculations.

## 10. Assumptions & Constraints
- The reporting hierarchy is maintained in Core HR (manager_id on the employee record).
- The approval workflow is configured by the Tenant Admin and can have 1-N levels.
- In Phase 1, delegation (manager delegates approval to another user during absence) is supported via the Workflow Engine's delegation feature.
- The system does not allow partial approval (e.g., approving clock-in but rejecting clock-out on the same request).
- Multi-tenant RLS ensures managers in Tenant A cannot see or act on requests from Tenant B.

## 11. Test Hints
- Test approval flow: submit regularization, approve as manager, verify attendance_log is updated.
- Test rejection flow: reject with reason, verify employee is notified with the reason.
- Test rejection without reason: verify the system requires a reason.
- Test multi-level workflow: approve at level 1, verify status stays "Pending" until level 2 approves.
- Test authorization: attempt to approve a request for a non-team employee, verify denial.
- Test self-approval prevention: manager submits own regularization, verify it routes to their supervisor.
- Test bulk approval: select 3 requests and approve all, verify all are processed.
- Test payroll lock: approve a request for a locked period date, verify it is blocked.
- Test multi-tenant isolation: verify manager in Tenant A cannot see requests from Tenant B.
- Test audit log: verify approval/rejection actions are logged with all required fields.
