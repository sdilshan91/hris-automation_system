---
id: US-ONB-002
module: Onboarding / Offboarding
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ONB-002: Assign Onboarding Checklist to New Hire

## 1. Description
**As an** HR Officer,
**I want to** assign an onboarding checklist to a newly hired employee based on their role and department,
**So that** the new hire, their manager, and IT receive a structured list of tasks with clear deadlines to complete before and after the joining date.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant.
- At least one active onboarding checklist template exists (US-ONB-001).
- The employee record exists with status `active` or `probation` and a valid `date_of_joining`.
- The Onboarding/Offboarding module is enabled for the tenant's subscription plan.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer opens an employee profile with no onboarding checklist assigned | They click "Assign Onboarding Checklist" | The system shows a list of active templates filtered by the employee's department and job title (plus universal templates), allowing selection. |
| AC-2 | HR Officer selects a template and confirms assignment | The checklist is assigned | Individual task instances are created for the employee, each with a calculated due date (date_of_joining + due_offset_days), status "pending", and the designated responsible party. A notification is sent to the employee, their manager, and assigned responsible parties via in-app (SignalR) and email. |
| AC-3 | HR Officer assigns a checklist to an employee who already has one | They attempt the assignment | The system warns: "This employee already has an active onboarding checklist. Do you want to replace it or add additional tasks?" with options to replace or merge. |
| AC-4 | HR Officer adds or removes individual tasks after assignment | They save the modifications | The checklist is updated; added tasks get new due dates based on today's date + offset; removed tasks are soft-deleted. An audit record captures the change. |
| AC-5 | The assigned checklist includes tasks for the Manager and IT roles | The assignment is saved | Notifications are dispatched to users with Manager role (the employee's reporting manager) and IT role within the tenant, informing them of their pending tasks via SignalR real-time push and email. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL auto-filter applicable templates based on the employee's department and job title.
- FR-2: The system SHALL create individual task instances from the template, calculating due dates as `date_of_joining + due_offset_days`.
- FR-3: The system SHALL resolve responsible parties: "Manager" maps to the employee's reporting_manager_id, "HR" maps to the assigning HR Officer, "IT" maps to users with the IT role, "Employee" maps to the new hire.
- FR-4: The system SHALL dispatch notifications (in-app via SignalR + email via Hangfire) to all responsible parties upon assignment.
- FR-5: The system SHALL allow HR to add ad-hoc tasks not in the original template.
- FR-6: The system SHALL allow HR to modify due dates on individual tasks after assignment.
- FR-7: The system SHALL set `tenant_id` from the session context on all created task instances.
- FR-8: The system SHALL track checklist assignment as an audit event.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Checklist assignment API response time SHALL be <= 1000 ms (P95), including task instance creation.
- NFR-2: All onboarding data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Notification dispatch SHALL use the outbox pattern (write intent in the same transaction, Hangfire worker dispatches).
- NFR-4: The assignment UI SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The operation SHALL be idempotent if retried within the same session.

## 6. Business Rules
- BR-1: Only active templates can be assigned; deactivated templates do not appear in the selection list.
- BR-2: An employee can have at most one active onboarding checklist at a time (replacing creates a new version).
- BR-3: Mandatory tasks from the template cannot be removed from the assigned checklist.
- BR-4: If the employee's date_of_joining is in the past, due dates are calculated from today's date instead.
- BR-5: Tasks assigned to the "Employee" role are only visible to the employee after their user account is linked.

## 7. Data Requirements
**Input fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| employee_id | uuid | Yes | Must exist in tenant |
| template_id | uuid | Yes | Must be active in tenant |
| override_start_date | date | No | Defaults to employee's date_of_joining |
| additional_tasks[] | object[] | No | Same schema as template tasks |

**Output:** Assigned checklist object with `checklist_instance_id`, employee reference, list of task instances with calculated due dates and statuses.

## 8. UI/UX Notes
- Template selection uses a searchable dropdown with department/job-title tag filters.
- After selection, show a preview of all tasks with calculated due dates in a timeline/Gantt-like view.
- Each task shows: title, responsible party avatar/role badge, due date, and status chip.
- Allow inline editing of due dates (date picker) and responsible party before confirming assignment.
- On mobile (< 768px): timeline view collapses to a vertical task list sorted by due date.
- Confirmation dialog with a summary of task count and responsible parties before final assignment.
- Success toast: "Onboarding checklist assigned. Notifications sent to N people."

## 9. Dependencies
- US-ONB-001: Onboarding checklist templates must exist.
- US-CHR-001: Employee record must exist.
- US-NTF-001: In-app notification system for real-time task alerts.
- US-NTF-002: Email notification templates for onboarding task emails.
- Authentication module: User must be authenticated with valid tenant context.

## 10. Assumptions & Constraints
- The employee's reporting manager is set in the employee record (via `reporting_manager_id`).
- If the employee does not yet have a linked user account, their tasks are held and become visible once the account is created and linked.
- Notification delivery uses the outbox pattern with Hangfire for reliability.
- Only free/open-source libraries are used.

## 11. Test Hints
- **Happy path:** Assign a template with 5 tasks to a new hire; verify 5 task instances created with correct due dates and `tenant_id`.
- **Auto-filter:** Create templates for Dept A and Dept B; verify employee in Dept A only sees Dept A and universal templates.
- **Duplicate assignment:** Assign a checklist, then assign another; verify the replace/merge prompt works correctly.
- **Notification dispatch:** Assign a checklist with Manager and IT tasks; verify SignalR notifications received by the manager and IT users, and email outbox entries created.
- **Tenant isolation:** Assign checklists in Tenant A and B; verify cross-tenant queries return nothing.
- **Past joining date:** Assign checklist to employee whose joining date was 10 days ago; verify due dates calculated from today.
- **Ad-hoc task:** Add a custom task not in the template; verify it persists alongside template tasks.
