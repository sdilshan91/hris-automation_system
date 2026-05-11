---
id: US-PRF-001
module: Performance Management
priority: Must Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-001: Manager Sets Goals/KPIs for Team Members

## 1. Description
**As a** Manager,
**I want to** set goals and KPIs for my team members at the start of an appraisal cycle,
**So that** each employee has clear, measurable objectives aligned with departmental and organizational targets, enabling fair and transparent performance evaluation.

## 2. Preconditions
- The manager is authenticated and has the `Performance.SetGoal.Team` permission.
- An active appraisal cycle exists for the current tenant (created by HR via US-PRF-004).
- The manager has at least one direct report assigned in the org tree.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An active appraisal cycle exists and the goal-setting window is open | The manager navigates to Performance > Goals for a team member | The system displays a goal-setting form with fields: title, description, category (KPI/competency/project), weight (%), target value, measurement unit, and due date |
| AC-2 | The manager has entered valid goal details with weights summing to 100% | The manager clicks "Save Goals" | The system persists all goals scoped to the tenant, links them to the employee and cycle, and sends an in-app notification to the employee |
| AC-3 | The manager attempts to save goals where weights do not sum to 100% | The manager clicks "Save Goals" | The system displays a validation error: "Goal weights must total 100%" and prevents submission |
| AC-4 | Goals have been saved for an employee | The manager views the team goals dashboard | The system displays all team members with goal-setting status (draft, submitted, acknowledged) with progress indicators |
| AC-5 | The goal-setting window has closed for the cycle | The manager attempts to add or edit goals | The system displays a read-only view with a message: "The goal-setting window for this cycle has closed" and prevents modifications |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall allow managers to create, edit, and delete goals for their direct reports during the goal-setting window of an active appraisal cycle.
- FR-2: Each goal shall have: title (max 200 chars), description (max 2000 chars), category (KPI, Competency, or Project), weight (1-100%), target value, measurement unit, and due date.
- FR-3: The system shall enforce that goal weights for a single employee within a cycle sum to exactly 100%.
- FR-4: The system shall support goal cascading: a manager can link an employee goal to a higher-level departmental or organizational objective.
- FR-5: The system shall allow managers to clone goals from a previous cycle or from a goal template library.
- FR-6: The system shall log all goal create/update/delete operations in the tenant audit trail.
- FR-7: The system shall notify the employee (in-app + email) when goals are assigned or modified.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Goal list for a team of up to 50 members shall load within 400ms (P95).
- NFR-2: All goal data shall be isolated per tenant via PostgreSQL RLS policies; a manager shall never see goals from another tenant.
- NFR-3: The goal-setting UI shall be fully responsive from 360px to 4K resolution, following Notion-like design aesthetics.
- NFR-4: Goal operations shall be protected with optimistic concurrency control to prevent lost updates when multiple sessions edit simultaneously.

## 6. Business Rules
- BR-1: Goals can only be set during the goal-setting phase of an appraisal cycle (date range configured by HR).
- BR-2: A minimum of 1 and maximum of 10 goals can be assigned per employee per cycle.
- BR-3: Goal weights must be in increments of 5%.
- BR-4: Only the direct reporting manager (or HR with `Performance.SetGoal.All`) can set goals for an employee.
- BR-5: Once an employee acknowledges their goals, the manager can only modify them with HR approval.

## 7. Data Requirements
- **Input:** goal title, description, category, weight, target value, measurement unit, due date, parent goal ID (optional for cascading), cycle ID, employee ID.
- **Output:** goal record with unique ID, audit timestamps (created_at, updated_at), created_by, tenant_id.
- **Storage:** Goals table with tenant_id column, RLS policy enforcing `tenant_id = current_setting('app.current_tenant_id')`.

## 8. UI/UX Notes
- Notion-like card layout for each goal with inline editing support.
- Drag-and-drop reordering of goals.
- Weight distribution shown as a horizontal stacked bar chart (chart.js).
- Goal cascade visualization as a collapsible tree view.
- Bulk goal assignment via a template selector.
- Mobile: swipeable goal cards with bottom-sheet for editing.

## 9. Dependencies
- US-PRF-004: HR creates and manages appraisal cycles (must exist first).
- Core HR module: employee records, org tree, department hierarchy.
- Notification system for goal assignment alerts.
- Audit logging module for tracking changes.

## 10. Assumptions & Constraints
- The org tree accurately reflects reporting relationships; goal assignment follows direct reporting lines.
- Goal templates are optional and can be configured by HR at the tenant level.
- The system does not auto-generate KPIs; all goals are manually defined by the manager.
- Angular 20 standalone components with signals are used for reactive UI updates.
- Tailwind CSS + Angular Material for styling (no Bootstrap).

## 11. Test Hints
- Verify tenant isolation: create goals in Tenant A, confirm they are invisible from Tenant B.
- Test weight validation: attempt to save goals with weights summing to 95%, 105%, and exactly 100%.
- Test goal-setting window enforcement: attempt to create goals before and after the window dates.
- Test concurrent editing: two browser sessions editing the same employee's goals simultaneously.
- Verify notification delivery: check in-app notification and email are sent on goal assignment.
- Test mobile responsiveness at 360px, 768px, and 1920px breakpoints.
- Test goal cascading: link employee goal to a department goal and verify the hierarchy displays correctly.
