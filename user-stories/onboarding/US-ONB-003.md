---
id: US-ONB-003
module: Onboarding / Offboarding
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ONB-003: New Hire Completes Onboarding Tasks

## 1. Description
**As a** newly hired Employee,
**I want to** view my onboarding checklist, complete assigned tasks, upload required documents, and track my progress,
**So that** I can fulfill all joining requirements on time and have a smooth start with the organization.

## 2. Preconditions
- The employee has an active user account linked to their employee record within the tenant.
- An onboarding checklist has been assigned to the employee (US-ONB-002).
- The employee is authenticated and has an active session within their tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The new hire logs in for the first time | They land on their dashboard | A prominent "Onboarding Progress" widget is displayed showing overall completion percentage, count of pending/completed/overdue tasks, and a link to the full checklist. |
| AC-2 | The new hire opens their onboarding checklist | They view the task list | Tasks are grouped by category, each showing title, description, due date, status (pending/in-progress/completed/overdue), and responsible party. Overdue tasks are highlighted in red. |
| AC-3 | The new hire marks a task as complete (e.g., "Read employee handbook") | They click "Mark Complete" | The task status changes to "completed", completion timestamp and actor are recorded, the overall progress percentage updates in real-time, and the HR Officer is notified via SignalR. |
| AC-4 | A task requires document upload (e.g., "Submit ID proof") | The employee uploads the document and marks the task complete | The document is stored in tenant-isolated object storage at `{tenantId}/onboarding/{employeeId}/{taskId}/{filename}`, the task status updates to "completed" with the file reference attached, and HR receives a notification. |
| AC-5 | A task is past its due date and not completed | The employee views the checklist | The task is marked as "overdue" with a red highlight and a warning icon. The HR Officer and the employee's manager have already received an automated overdue notification (dispatched by Hangfire scheduled job). |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL display a personalized onboarding checklist for the logged-in employee, showing only their assigned tasks.
- FR-2: The system SHALL allow the employee to mark tasks assigned to them as "completed" with an optional comment.
- FR-3: The system SHALL support file upload on tasks that require document submission, with MIME type and size validation.
- FR-4: The system SHALL calculate and display an overall progress percentage (completed / total tasks).
- FR-5: The system SHALL send real-time notifications (SignalR) to HR and the employee's manager when a task is completed.
- FR-6: The system SHALL run a Hangfire scheduled job (daily) to detect overdue tasks and send notifications to the employee, HR, and manager.
- FR-7: The system SHALL prevent the employee from marking tasks assigned to other roles (e.g., IT, Manager) as complete.
- FR-8: The system SHALL log task completion events in the tenant audit log.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Checklist loading API response time SHALL be <= 500 ms (P95).
- NFR-2: All onboarding data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Document uploads SHALL be scanned for malware (ClamAV) before persistence.
- NFR-4: The checklist UI SHALL be fully responsive from 360px to 4K resolution, optimized for mobile-first (new hires may use phones).
- NFR-5: The UI SHALL meet WCAG 2.1 AA accessibility standards.
- NFR-6: File uploads SHALL be limited to 10 MB per file (configurable per tenant plan).

## 6. Business Rules
- BR-1: Employees can only complete tasks assigned to the "Employee" role; tasks for "HR", "Manager", or "IT" are read-only for the employee.
- BR-2: Mandatory tasks must be completed before the onboarding checklist can be marked as "fully complete".
- BR-3: Once a task is marked as "completed", it cannot be reverted to "pending" by the employee (only HR can reopen).
- BR-4: Overdue notifications are sent once per day at a tenant-configurable time (default: 9:00 AM tenant timezone).
- BR-5: Document uploads are retained for the duration of employment plus the tenant's data retention policy.

## 7. Data Requirements
**Input (task completion):**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| task_instance_id | uuid | Yes | Must belong to the employee in the tenant |
| status | varchar(20) | Yes | "completed" |
| comment | text | No | Max 500 chars |
| attachment | file | Conditional | Required if task type = "document_upload"; max 10 MB |

**Output:** Updated task instance with completion timestamp, actor, attachment URL (if any), and recalculated checklist progress.

## 8. UI/UX Notes
- Dashboard widget: circular progress ring showing completion percentage, with a task count summary below.
- Checklist view: Notion-like task list with checkboxes; completed tasks show strikethrough text and a green checkmark.
- Categories are collapsible accordion sections with task count badges (e.g., "Documentation 2/4").
- Document upload: drag-and-drop zone within the task card, with file type icons and a progress bar.
- Overdue tasks: red border on the task card, warning icon, and "X days overdue" badge.
- Smooth animations: checkbox check animation (scale + green fill), progress ring increment.
- On mobile (< 768px): full-width task cards, single-column layout, bottom sheet for file upload.
- Confetti or success illustration when 100% of tasks are completed.

## 9. Dependencies
- US-ONB-002: Onboarding checklist must be assigned to the employee.
- US-NTF-001: In-app notification system for real-time updates.
- US-NTF-002: Email notification templates for overdue reminders.
- File & Document Management (Technical Doc S26): For document storage.
- Authentication module: User must be authenticated with valid tenant context.

## 10. Assumptions & Constraints
- The employee's user account has been created and linked to their employee record before they can access the checklist.
- Object storage (Azure Blob / S3 / MinIO) is available for document uploads.
- Hangfire is configured and running for scheduled overdue detection jobs.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Log in as new hire, view checklist, complete 3 of 5 tasks; verify progress shows 60% and audit records exist.
- **Document upload:** Upload a valid PDF for a document-required task; verify file stored at correct tenant-isolated path.
- **Overdue detection:** Set a task due date to yesterday, run the Hangfire job; verify overdue notification sent to employee, HR, and manager.
- **Role restriction:** Attempt to complete an IT-assigned task as the employee; expect the action to be blocked.
- **Tenant isolation:** Verify employee in Tenant A cannot see or interact with Tenant B onboarding tasks.
- **Mandatory tasks:** Complete all optional tasks but leave a mandatory one; verify checklist is not marked "fully complete".
- **Responsive:** Test the checklist at 360px width; verify task cards stack vertically and document upload works via mobile.
- **Accessibility:** Navigate the entire checklist using keyboard only; verify screen reader announces task statuses and due dates.
