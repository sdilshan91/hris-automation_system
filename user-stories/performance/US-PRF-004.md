---
id: US-PRF-004
module: Performance Management
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-004: HR Creates and Manages Appraisal Cycles

## 1. Description
**As an** HR Officer,
**I want to** create and manage appraisal cycles with configurable phases, timelines, and participants,
**So that** the organization follows a structured, time-bound performance review process that is consistent across departments and transparent to all stakeholders.

## 2. Preconditions
- The HR Officer is authenticated and has `Performance.SetGoal.All` and `Performance.Publish.All` permissions.
- The Performance module is enabled for the tenant.
- The tenant has configured a rating scale in Performance module settings.
- Employee records and org tree are set up in Core HR.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The HR Officer navigates to Performance > Cycles | The HR Officer clicks "Create New Cycle" | The system displays a cycle creation form with fields: cycle name, period (start/end dates), phases (goal-setting, self-assessment, manager-review, calibration, publish) each with their own date ranges, participant scope (all employees, specific departments, specific grades), rating scale selection, and 360-degree toggle |
| AC-2 | The HR Officer has filled in valid cycle details with non-overlapping phase dates | The HR Officer clicks "Create Cycle" | The system creates the cycle, schedules Hangfire background jobs for phase-transition notifications and deadline reminders, and displays a confirmation |
| AC-3 | An active cycle exists | The HR Officer views the cycle dashboard | The system displays a timeline view of phases, completion statistics per phase (% of employees who completed goal-setting, self-assessment, manager-review), and overdue counts |
| AC-4 | A cycle phase deadline is approaching | The Hangfire job triggers at the configured reminder interval | The system sends automated reminder notifications (in-app + email) to all participants who have not completed the current phase |
| AC-5 | The HR Officer needs to extend a phase deadline | The HR Officer edits the cycle and changes a phase end date | The system validates that phase dates remain sequential and non-overlapping, updates the Hangfire job schedules accordingly, and notifies affected participants |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall support creating appraisal cycles with a minimum of three phases: goal-setting, assessment (self + manager), and publish.
- FR-2: Each phase shall have configurable start and end dates; phases must be sequential and non-overlapping within a cycle.
- FR-3: The system shall allow HR to scope a cycle to: all active employees, specific departments, specific grades/bands, or a custom employee list.
- FR-4: The system shall support multiple concurrent cycles (e.g., annual review + quarterly KPI check-in) as long as the same employee is not in overlapping cycles of the same type.
- FR-5: The system shall integrate with Hangfire to schedule: phase-start notifications, deadline reminders (configurable intervals), phase-close notifications, and overdue escalation alerts.
- FR-6: The system shall allow HR to configure: rating scale, self vs. manager weight ratio, 360-degree toggle, calibration phase toggle, and anonymity settings for peer feedback.
- FR-7: The system shall support cycle statuses: Draft, Active, Paused, Completed, Cancelled.
- FR-8: The system shall allow HR to clone an existing cycle as a template for the next period.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Cycle creation with up to 5,000 participants shall complete within 5 seconds.
- NFR-2: All cycle data shall be tenant-isolated via PostgreSQL RLS; cycles from one tenant shall never be visible to another.
- NFR-3: Hangfire jobs for notifications shall be scoped to the tenant and resilient to failures (retry with exponential backoff via Polly).
- NFR-4: The cycle dashboard shall load within 2 seconds (P95) including aggregate statistics.

## 6. Business Rules
- BR-1: Only users with `Performance.SetGoal.All` or `Performance.Publish.All` can create or modify cycles.
- BR-2: A cycle cannot be deleted if any reviews have been submitted; it can only be cancelled.
- BR-3: Phase dates must be within the cycle's overall start and end dates.
- BR-4: An employee cannot be a participant in two active cycles of the same type simultaneously.
- BR-5: The rating scale is locked once the cycle transitions from Draft to Active status.
- BR-6: Cycle cancellation requires a reason and sends notifications to all participants.

## 7. Data Requirements
- **Input:** cycle name, type (annual/quarterly/probation), start date, end date, phase definitions (name, start, end), participant scope, rating scale ID, weight configuration, 360-degree toggle, calibration toggle.
- **Output:** cycle record with unique ID, status, participant count, phase completion statistics, scheduled job IDs, tenant_id.
- **Storage:** Cycles table, cycle_phases table, cycle_participants table, all with tenant_id and RLS policies.

## 8. UI/UX Notes
- Notion-like timeline/Gantt view for phase visualization within a cycle.
- Drag-and-drop phase date adjustment on the timeline.
- Dashboard with donut charts (chart.js) showing completion rates per phase.
- Participant scope selector with department tree picker and employee search.
- Status badges with color coding: Draft (gray), Active (green), Paused (amber), Completed (blue), Cancelled (red).
- Mobile: simplified timeline as vertical stepper, swipeable stats cards.

## 9. Dependencies
- Core HR: employee records, departments, grades for participant scoping.
- Notification system for phase reminders and alerts.
- Hangfire for scheduling background notification jobs.
- Performance module settings: rating scales, weight configuration.

## 10. Assumptions & Constraints
- Hangfire is the sole job scheduler; no external schedulers are used.
- Phase transitions are date-driven and automatically enforced by a Hangfire recurring job.
- The tenant admin configures rating scales before creating cycles.
- PostgreSQL with Hangfire.PostgreSql for job persistence.
- Cycle templates reduce setup effort for recurring review periods.

## 11. Test Hints
- Verify tenant isolation: create a cycle in Tenant A, confirm it is invisible in Tenant B.
- Test phase date validation: attempt overlapping phases, out-of-range dates, and reversed start/end dates.
- Test Hangfire job scheduling: create a cycle, verify reminder jobs are registered with correct cron expressions.
- Test cycle status transitions: Draft -> Active -> Paused -> Active -> Completed, and Draft -> Cancelled.
- Test participant scoping: scope to a department, add an employee from another department, verify rejection.
- Test concurrent cycles: attempt to add an employee to two annual cycles simultaneously.
- Test cycle cloning: clone a completed cycle, verify all settings are copied with new dates.
- Load test: create a cycle with 5,000 participants, verify creation completes within SLA.
