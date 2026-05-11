---
id: US-ONB-005
module: Onboarding / Offboarding
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ONB-005: Offboarding / Exit Checklist and Clearance

## 1. Description
**As an** HR Officer,
**I want to** initiate an offboarding process for a departing employee with an exit checklist covering asset returns, access revocation, knowledge transfer, and departmental clearances,
**So that** the organization ensures all company assets are recovered, access is revoked, and compliance obligations are met before the employee's last working day.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant.
- The employee record exists with a resignation accepted or termination status.
- The Onboarding/Offboarding module is enabled for the tenant's subscription plan.
- The employee's last working day (LWD) is set in the system.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer opens a departing employee's profile | They click "Initiate Offboarding" | An exit checklist is generated based on the tenant's offboarding template, with tasks assigned to HR, IT, Finance, the employee's manager, and the employee. Each task has a due date calculated relative to the last working day. |
| AC-2 | The exit checklist includes an "Asset Return" task for IT | IT marks assets as returned | The asset status in the register changes from "assigned" to "available" (or "disposed"), the task is marked complete, and an audit record is created. |
| AC-3 | The exit checklist includes clearance tasks from multiple departments (IT, Finance, Admin) | Each department head marks their clearance as "approved" or "pending issues" | The overall clearance status is calculated: "fully cleared" only when all departments have approved. HR sees a clearance dashboard with department-wise status. |
| AC-4 | All clearance tasks are marked as approved and all mandatory tasks are complete | HR clicks "Complete Offboarding" | The employee status is changed to "terminated" (or "resigned"), the user account is deactivated (cannot log in), and a Full & Final (F&F) settlement trigger notification is sent to Payroll. |
| AC-5 | HR attempts to complete offboarding with pending mandatory tasks | They click "Complete Offboarding" | The system blocks the action and displays: "Cannot complete offboarding. The following mandatory items are pending: [list]." |
| AC-6 | An offboarding record is created in Tenant A | A user from Tenant B queries the offboarding data | No Tenant A data is visible; RLS and EF Core filters enforce tenant isolation. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide offboarding checklist templates configurable per tenant (similar structure to onboarding templates).
- FR-2: The system SHALL auto-generate exit task instances from the template, with due dates calculated relative to the employee's last working day (LWD - offset_days).
- FR-3: The system SHALL include built-in clearance categories: IT (access revocation, asset return), Finance (advances, loans, F&F), Admin (ID card, parking, keys), Manager (knowledge transfer, handover).
- FR-4: The system SHALL provide a clearance dashboard showing department-wise approval status with visual indicators (green = cleared, red = pending, yellow = issues).
- FR-5: The system SHALL deactivate the employee's user account upon offboarding completion (prevent future logins).
- FR-6: The system SHALL trigger an F&F settlement notification to Payroll upon offboarding completion.
- FR-7: The system SHALL revoke all active sessions (SignalR disconnect + JWT blacklist via Redis) for the departing employee upon offboarding completion.
- FR-8: The system SHALL set `tenant_id` from the session context on all offboarding records.
- FR-9: The system SHALL record all offboarding actions in the tenant audit log.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Offboarding initiation API response time SHALL be <= 1000 ms (P95).
- NFR-2: All offboarding data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: User account deactivation and session revocation SHALL complete within 30 seconds of offboarding completion.
- NFR-4: The clearance dashboard SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The offboarding process SHALL meet WCAG 2.1 AA accessibility standards.

## 6. Business Rules
- BR-1: Offboarding can only be initiated for employees with status "resignation_accepted", "terminated", or "contract_ended".
- BR-2: All mandatory clearance tasks must be approved before offboarding can be completed.
- BR-3: Asset return tasks automatically update the asset register status.
- BR-4: F&F settlement calculation is handled by the Payroll module; offboarding only triggers the notification.
- BR-5: The employee's data is retained per the tenant's data retention policy; only the user account is deactivated.
- BR-6: Offboarding completion is irreversible; the employee record cannot be reactivated (a new record must be created for rehires).

## 7. Data Requirements
**Input fields (initiation):**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| employee_id | uuid | Yes | Must exist in tenant with appropriate status |
| last_working_day | date | Yes | Must be today or in the future |
| offboarding_template_id | uuid | No | If not provided, use default tenant template |
| reason | varchar(50) | Yes | Resignation, Termination, Contract End, Retirement |
| notes | text | No | Max 2000 chars |

**Clearance input (per department):**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| clearance_task_id | uuid | Yes | Must belong to this offboarding instance |
| status | varchar(20) | Yes | "approved" or "pending_issues" |
| remarks | text | No | Max 1000 chars |

**Output:** Offboarding instance with task list, clearance statuses, and overall progress.

## 8. UI/UX Notes
- Offboarding dashboard: Kanban-like board with columns for each clearance department, showing task cards with status chips.
- Clearance status: traffic light icons (green/yellow/red) per department in a summary bar at the top.
- Asset return section: list of currently assigned assets with "Mark Returned" button and condition dropdown.
- "Complete Offboarding" button is disabled (greyed out) until all mandatory tasks are done; hovering shows remaining items.
- Timeline view showing key dates: resignation date, notice period, LWD, and clearance milestones.
- On mobile (< 768px): Kanban collapses to a vertical accordion grouped by department.
- Confirmation modal before final offboarding completion with a summary of all actions that will be taken (account deactivation, F&F trigger).

## 9. Dependencies
- US-ONB-001: Offboarding templates follow the same structure as onboarding templates.
- US-ONB-004: Asset register for tracking asset returns.
- US-CHR-001: Employee record must exist.
- US-NTF-001: In-app notifications for clearance approvals and F&F trigger.
- US-NTF-002: Email notification templates for offboarding communications.
- Payroll module: For F&F settlement processing (triggered, not managed here).
- Authentication module: For user account deactivation and session revocation.

## 10. Assumptions & Constraints
- Offboarding templates are configured by the Tenant Admin, similar to onboarding templates.
- F&F settlement calculation logic resides in the Payroll module; this story only triggers it.
- Session revocation uses Redis-based JWT blacklisting (add the user's tokens to a deny list in Redis).
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Initiate offboarding, complete all clearance tasks, complete offboarding; verify employee status = "terminated", user account deactivated, F&F notification sent.
- **Blocked completion:** Leave a mandatory clearance task pending; attempt to complete offboarding; expect block with specific pending items listed.
- **Asset return:** Mark a laptop as returned during offboarding; verify asset register status = "available".
- **Session revocation:** Complete offboarding; attempt to use the employee's existing JWT; expect 401 Unauthorized.
- **Tenant isolation:** Initiate offboarding in Tenant A; query from Tenant B; expect no results.
- **Clearance dashboard:** Assign clearance tasks to 4 departments; approve 2, leave 2 pending; verify dashboard shows correct traffic-light status.
- **Audit trail:** Verify each clearance approval and the final offboarding completion create audit log entries.
- **Responsive:** Test the clearance dashboard at 360px and 1920px widths.
