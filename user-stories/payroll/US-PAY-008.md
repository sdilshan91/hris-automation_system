---
id: US-PAY-008
module: Payroll
priority: Must Have
persona: HR Officer / Finance Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-008: Payroll Approval Workflow

## 1. Description
**As an** HR Officer or Finance Manager,
**I want to** submit a completed payroll run for review and approval before it is finalized,
**So that** payroll disbursement goes through proper authorization and any errors can be caught before funds are released.

## 2. Preconditions
- A payroll run exists in ReviewPending status (US-PAY-003).
- The tenant has configured a payroll approval workflow (or uses the default: HR submits, Finance approves).
- Approver has `Payroll.Approve` permission.
- The approval workflow engine is operational (technical doc section 34).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A payroll run is in ReviewPending status | HR reviews the payroll summary (total gross, deductions, net, employee count) and clicks "Submit for Approval" | A workflow instance is created; the payroll run status changes to AwaitingApproval; the designated approver(s) receive an in-app notification and email |
| AC-2 | A payroll run is in AwaitingApproval status | The Finance Manager reviews the run summary and employee-level details and clicks "Approve" | The payroll run status changes to Approved; HR is notified; the run can now be finalized |
| AC-3 | A payroll run is in AwaitingApproval status | The approver identifies discrepancies and clicks "Reject" with a reason | The payroll run status changes to Rejected; HR is notified with the rejection reason; HR can make adjustments and re-submit |
| AC-4 | The approval workflow has multiple steps (e.g., HR Manager then Finance Director) | HR submits the payroll run | The workflow engine routes the approval sequentially through each step; the run is only Approved when all steps are completed |
| AC-5 | A payroll run is Approved | HR clicks "Finalize" | The payroll run status changes to Finalized; records become immutable; the run is ready for bank advice generation and payslip distribution |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL integrate with the platform's approval workflow engine (technical doc section 34) for payroll run approval.
- FR-2: The system SHALL support configurable approval workflows per tenant with one or more sequential or parallel approval steps.
- FR-3: Each approval step SHALL have a configurable SLA (e.g., approve within 24 hours) with auto-escalation to a backup approver if the SLA is breached.
- FR-4: Approvers SHALL be able to view a comprehensive payroll summary including: total employees, total gross, total deductions, total statutory, total net, comparison with previous month (variance), and a list of exceptions/warnings.
- FR-5: Approvers SHALL be able to drill down into individual employee payslips from the approval review page.
- FR-6: The system SHALL support approval delegation (approver can delegate to another authorized user during their absence).
- FR-7: The system SHALL maintain a complete audit trail of all approval actions: who approved/rejected, when, comments/reasons, IP address.
- FR-8: Upon finalization, the system SHALL lock all payslip records for the run, making them immutable.
- FR-9: The system SHALL support a "Return to HR" action where the approver sends the run back with specific comments without formally rejecting it.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Approval notifications SHALL be delivered within 30 seconds of submission (in-app via SignalR, email via notification service).
- NFR-2: The approval review page SHALL load within 2 seconds, including the payroll summary and exception list.
- NFR-3: PRs that modify payroll approval logic require 2 reviewers (per technical doc section 44).
- NFR-4: Test coverage for approval workflow integration SHALL be >= 85%.
- NFR-5: All approval/rejection actions SHALL be audit-logged with tamper-proof timestamps.

## 6. Business Rules
- BR-1: A payroll run MUST go through at least one approval step before it can be finalized. Direct finalization without approval is not permitted.
- BR-2: The default approval workflow is: HR submits -> Finance Manager approves. Tenants can customize this.
- BR-3: A rejected payroll run can be corrected (adjustments made, re-calculated) and re-submitted. The re-submission creates a new workflow instance.
- BR-4: Payroll run status transitions for approval: ReviewPending -> AwaitingApproval -> Approved -> Finalized. Also: AwaitingApproval -> Rejected -> ReviewPending (after corrections).
- BR-5: An approver cannot approve a payroll run that they themselves initiated (maker-checker principle), unless the tenant has fewer than 2 HR/Finance users (small team exception).
- BR-6: The Finalized status is terminal and irreversible. No further status changes are possible.
- BR-7: If a payroll run is Approved but not Finalized within a configurable period (default 7 days), a reminder notification is sent to HR.
- BR-8: The approval workflow must be tenant-scoped; Tenant A's workflow does not affect Tenant B.

## 7. Data Requirements

**payroll_approval_history table:**
| Column | Type | Constraints |
|--------|------|-------------|
| approval_history_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| payroll_run_id | uuid (FK) | NOT NULL |
| workflow_instance_id | uuid (FK) | NOT NULL |
| step_number | int | NOT NULL |
| action | varchar(20) | NOT NULL (Submitted, Approved, Rejected, Returned, Escalated) |
| actor_user_id | uuid (FK) | NOT NULL |
| comments | text | nullable |
| acted_at | timestamptz | NOT NULL |
| ip_address | varchar(50) | nullable |

**Payroll Run Summary (for approval review):**
| Field | Description |
|-------|-------------|
| total_employees | Number of employees in this run |
| total_gross | Sum of all gross earnings |
| total_deductions | Sum of all deductions |
| total_statutory | Sum of all statutory deductions |
| total_net | Sum of all net salaries |
| previous_month_total_net | Previous month's total for variance |
| variance_percentage | Month-over-month change |
| exceptions | List of warnings (missing structures, negative net, etc.) |

## 8. UI/UX Notes (Notion-like)
- Approval queue: a dedicated "Pending Approvals" section in the approver's dashboard with a badge count. Notion-style card for each pending payroll run showing period, totals, and submitted-by info.
- Approval review page: split layout -- left panel shows the payroll summary card with key metrics and variance from last month; right panel shows the employee payslip list (searchable, filterable).
- Approve/Reject/Return buttons in a sticky bottom action bar with required comments field for Reject and Return actions.
- Comparison view: side-by-side metrics with previous month, with variance highlighted (green for decrease, amber for increase > 5%, red for increase > 15%).
- Timeline view of the approval history at the bottom of the payroll run detail page.
- Mobile: approvers can view summary and approve/reject from mobile; detailed employee drill-down deferred to desktop.

## 9. Dependencies
- **US-PAY-003**: Payroll run must be in ReviewPending status.
- **US-PAY-004**: Payslip details must be available for approver drill-down.
- **Approval Workflow Engine**: Technical doc section 34 -- the payroll module integrates with the shared workflow engine.
- **Notification System**: Technical doc section 25 -- email and in-app notifications for approval events.
- SignalR for real-time notification delivery.

## 10. Assumptions & Constraints
- The approval workflow engine (technical doc section 34) is a shared platform component used across modules (leave, attendance, payroll, etc.).
- Payroll approval is a more critical workflow than leave approval; the system must treat it with higher priority in notification delivery.
- In small tenants with a single HR user, the maker-checker rule can be relaxed via tenant configuration.
- The system does not integrate with external approval systems (e.g., email-based approval) in Phase 1.

## 11. Test Hints
- Unit test: Verify payroll run status transitions follow the defined state machine (no invalid transitions).
- Unit test: Verify maker-checker rule blocks the initiator from approving their own run.
- Integration test: Submit payroll for approval, verify workflow instance is created and notification is sent.
- Integration test: Approve a payroll run, verify status changes to Approved and HR is notified.
- Integration test: Reject a payroll run with reason, verify status changes to Rejected and reason is stored in audit trail.
- Integration test: Verify multi-step approval workflow routes sequentially through all steps.
- E2E (Playwright): Full flow -- HR initiates payroll, submits for approval, Finance Manager logs in and approves, HR finalizes.
- E2E: Test rejection flow -- Finance rejects with comments, HR sees comments, makes adjustments, re-submits.
