---
id: US-CHR-011
module: Core HR
priority: Must Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-011: Employee Reporting Structure (Manager Assignment)

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** assign a reporting manager to each employee and manage the reporting hierarchy,
**So that** approval workflows (leave, attendance, performance) route to the correct manager, and the organization's chain of command is clearly defined.

## 2. Preconditions
- The user is authenticated with HR Officer or Tenant Admin role within their tenant.
- At least two employee records exist (one to be the manager, one to be the report).
- The manager employee must have `active` status.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer opens an employee's profile (Employment Details section) | They see a "Reporting Manager" field | The field shows the current manager (avatar + name) or "Not Assigned". An edit button allows selecting a new manager via an employee search/autocomplete. |
| AC-2 | The HR Officer assigns a manager to an employee and saves | The form is submitted | The `manager_employee_id` (or equivalent FK) is updated on the employee record; the change is recorded in the employment history timeline; the audit log captures before/after values. |
| AC-3 | An HR Officer attempts to create a circular reporting chain (e.g., A reports to B, B reports to A) | They try to save | The system detects the cycle and rejects with: "Circular reporting chain detected. [Employee A] cannot report to [Employee B] because [Employee B] already reports to [Employee A] (directly or indirectly)." |
| AC-4 | A Manager views their team dashboard | The page loads | They see a list of all direct reports (employees who have them as reporting manager), with name, job title, department, status, and quick-action links (view profile, approve leave). |
| AC-5 | An HR Officer bulk-assigns managers via the employee directory | They select multiple employees and choose "Assign Manager" from a bulk action menu | A modal appears to select a manager; on confirmation, all selected employees are updated with the new reporting manager, changes are logged individually for each employee. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL store the reporting manager as an FK (`manager_employee_id` or `reports_to_employee_id`) on the employee record, nullable.
- FR-2: The system SHALL support assigning one direct reporting manager per employee.
- FR-3: The system SHALL detect and prevent circular reporting chains (cycle detection) up to any depth.
- FR-4: The system SHALL support bulk manager assignment for multiple employees at once.
- FR-5: The system SHALL provide a "My Team" / direct reports view for managers showing all employees who report to them.
- FR-6: The system SHALL record every manager assignment change in the employment history timeline.
- FR-7: The system SHALL propagate manager assignments to approval workflows (leave, attendance, performance) automatically.
- FR-8: The system SHALL allow an employee to have no manager assigned (e.g., the CEO / top-level person).
- FR-9: All queries SHALL be scoped by `tenant_id` via RLS and EF Core global query filters.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Manager assignment API response time SHALL be <= 800 ms (P95), including cycle detection.
- NFR-2: Cycle detection SHALL complete within 200 ms for hierarchies up to 500 employees deep.
- NFR-3: All reporting structure data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-4: The manager assignment UI SHALL be fully responsive (360px to 4K).
- NFR-5: Manager assignment changes SHALL be audited with before/after snapshots.
- NFR-6: Bulk manager assignment for up to 100 employees SHALL complete within 5 seconds.

## 6. Business Rules
- BR-1: An employee can have at most one direct reporting manager.
- BR-2: A manager can have unlimited direct reports (no system-enforced limit).
- BR-3: Only `active` employees can be assigned as managers.
- BR-4: If a manager is terminated or suspended, the system sends a notification to HR to reassign their direct reports.
- BR-5: The reporting hierarchy is independent of the department hierarchy: an employee can report to a manager in a different department.
- BR-6: Manager assignment determines the approval chain for leave requests, attendance regularizations, and performance reviews.
- BR-7: Self-assignment (employee reports to themselves) is not allowed.

## 7. Data Requirements
**Employee table addition:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| reports_to_employee_id | uuid (FK) | No | Self-referencing FK to employee table |

**Direct reports API endpoint:** `GET /api/v1/employees/{managerId}/direct-reports`

**Response:** Array of employee summary objects (employee_id, name, job title, department, status, avatar_url).

## 8. UI/UX Notes (Notion-like, cards-based)
- Reporting Manager field on the employee profile: displayed as a mini-card (avatar 32px + name + job title) within the Employment Details card. Click to edit.
- Manager selector: modal or dropdown with employee search/autocomplete, showing avatar, name, department, and job title. Only active employees shown.
- "My Team" section for managers: card-based list of direct reports, each as a compact card (avatar, name, title, status badge). Quick actions on hover: View Profile, Approve Leave.
- Bulk assignment: checkbox selection in the employee directory table, then "Assign Manager" from a floating action toolbar at the bottom of the screen.
- Reporting chain breadcrumb: on an employee's profile, show the full reporting chain upward (Employee -> Manager -> Manager's Manager -> ... -> Top) as a horizontal breadcrumb trail.
- On mobile: manager selector is a full-screen search overlay; "My Team" cards stack vertically; bulk actions accessible via a bottom sheet.
- Smooth animations on card transitions and search results filtering (200ms ease).

## 9. Dependencies
- US-CHR-001: Employee records must exist for both the report and the manager.
- US-CHR-002: Manager assignment is displayed and edited on the employee profile.
- US-CHR-006: Reporting structure feeds into the org tree "Reporting Structure" view.
- US-CHR-009: Manager status changes trigger reassignment notifications.
- Leave module (future): Approval routing uses reporting manager.
- Attendance module (future): Regularization approval uses reporting manager.
- Performance module (future): Appraisal routing uses reporting manager.

## 10. Assumptions & Constraints
- Cycle detection is performed server-side by traversing the reporting chain from the proposed manager upward to the root; if the current employee is encountered, the assignment is rejected.
- The reporting hierarchy is separate from the department hierarchy; they may overlap but are not required to align.
- Dotted-line / secondary reporting relationships are not supported in Phase 1.
- Only free/open-source libraries are used.

## 11. Test Hints
- **Assign manager:** Assign Manager M to Employee E; verify FK is set; verify M's direct reports list includes E.
- **Circular chain:** Create A -> B -> C; attempt C -> A; expect rejection with clear error.
- **Self-assignment:** Attempt to set an employee as their own manager; expect rejection.
- **Bulk assignment:** Select 5 employees; assign same manager; verify all 5 updated with individual audit entries.
- **Manager termination:** Terminate a manager with 3 direct reports; verify HR is notified to reassign.
- **Tenant isolation:** Assign managers in Tenant A; verify no cross-tenant leakage in direct reports queries.
- **Deep hierarchy:** Create a 10-level deep chain; verify cycle detection still performs within 200 ms.
- **No manager:** Create an employee with no manager; verify it works (nullable FK); verify they appear as a root node in org tree.
- **Audit trail:** Assign and reassign a manager; verify 2 employment history entries with before/after values.
