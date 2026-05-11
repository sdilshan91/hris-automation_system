---
id: US-PRF-009
module: Performance Management
priority: Should Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-009: Goal Tracking with Progress Updates

## 1. Description
**As an** Employee,
**I want to** track my progress against assigned goals throughout the appraisal cycle by posting regular updates, logging milestones, and viewing my goal completion status,
**So that** I have a continuous record of my achievements, can proactively communicate progress to my manager, and am well-prepared for the formal review with documented evidence.

## 2. Preconditions
- The employee is authenticated and has `Performance.Read.Self` permission.
- Goals have been assigned and acknowledged for the current appraisal cycle (US-PRF-001).
- The appraisal cycle is in an active phase (between goal-setting close and review phase start).
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The employee has active goals in the current cycle | The employee navigates to Performance > My Goals | The system displays all goals as cards with: title, target, current progress (% complete), status (not started, in progress, completed, at risk), last update date, and a progress bar |
| AC-2 | The employee wants to log a progress update | The employee clicks "Add Update" on a goal card | The system displays a form with: progress percentage slider, status selector, update notes (rich text), and optional file attachment for evidence; on save, the update is timestamped, logged, and the manager receives a notification |
| AC-3 | The employee has posted multiple updates on a goal | The employee expands the goal card to view update history | The system displays a chronological timeline of all updates with date, progress change, notes, and attachments |
| AC-4 | The manager views their team's goal progress | The manager navigates to Performance > Team Goals | The system displays a summary table with each team member, their overall goal completion percentage, number of goals at risk, and last update date; the manager can drill down to individual employee goals and updates |
| AC-5 | A goal's progress has not been updated for a configurable period | Hangfire detects the stale goal based on tenant configuration | The system sends a nudge notification to the employee: "You haven't updated progress on [Goal Title] in [X] days" and flags the goal as "Needs Attention" on the manager's dashboard |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall allow employees to update goal progress at any time during the active cycle period.
- FR-2: Each progress update shall capture: progress percentage (0-100%), status (not started, in progress, completed, at risk, blocked), update notes (max 2000 chars), and optional file attachments (max 3 files, 10MB each).
- FR-3: The system shall maintain a full history of progress updates per goal, displayed as a timeline.
- FR-4: The system shall calculate an overall goal completion percentage as a weighted average of individual goal progress percentages.
- FR-5: The system shall send real-time notifications (via SignalR if available, else polling) to the manager when an employee posts a progress update.
- FR-6: Hangfire shall run a daily job to detect stale goals (no update in X days, tenant-configurable, default: 14 days) and send nudge notifications.
- FR-7: The system shall support goal status transitions: Not Started -> In Progress -> Completed (or At Risk / Blocked at any point).
- FR-8: Managers shall be able to comment on employee progress updates, creating a conversation thread per goal.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Goal list with progress data shall load within 400ms (P95) for up to 10 goals.
- NFR-2: All goal progress data shall be tenant-isolated via PostgreSQL RLS.
- NFR-3: Progress update history shall be append-only; updates cannot be deleted or modified after submission (audit compliance).
- NFR-4: The goal tracking UI shall be optimized for mobile use, as employees may post updates from their phones.
- NFR-5: Stale goal detection Hangfire job shall process all active goals for a tenant with up to 5,000 employees within 60 seconds.

## 6. Business Rules
- BR-1: Progress updates can only be posted during the active cycle period (between goal-setting close and review phase start).
- BR-2: Setting a goal's progress to 100% automatically changes its status to "Completed" (can be overridden by the employee).
- BR-3: A goal marked as "Blocked" triggers a notification to the manager and HR.
- BR-4: The stale goal nudge interval is tenant-configurable (default: 14 days); setting it to 0 disables nudge notifications.
- BR-5: Progress updates are visible to the employee, their manager, and HR; they are not visible to peers unless the tenant enables shared goal visibility.

## 7. Data Requirements
- **Input:** goal_id, progress percentage, status, update notes, file attachments, manager comments.
- **Output:** progress update record with timestamp, updated overall completion percentage, notification events.
- **Storage:** goal_progress_updates table (append-only) with goal_id, employee_id, progress_pct, status, notes, created_at, tenant_id with RLS policy. Manager comments in goal_comments table.

## 8. UI/UX Notes
- Notion-like goal cards with animated progress bars (CSS transitions).
- Progress percentage input as a slider with percentage label.
- Status selector as color-coded chips: gray (not started), blue (in progress), green (completed), amber (at risk), red (blocked).
- Update timeline with avatar, timestamp, and expandable notes.
- Manager comment thread below each update (similar to Notion comments).
- Overall completion dashboard widget with a chart.js donut chart.
- Mobile: swipeable goal cards, bottom-sheet for adding updates, pull-to-refresh.

## 9. Dependencies
- US-PRF-001: Goals must be assigned before tracking can begin.
- US-PRF-004: Active appraisal cycle defines the tracking period.
- Notification system for update alerts and stale goal nudges.
- Hangfire for daily stale goal detection job.
- File management module for evidence attachments.
- SignalR (optional) for real-time update notifications to managers.

## 10. Assumptions & Constraints
- Progress tracking is a continuous activity, not tied to specific review phases.
- The append-only update history ensures a tamper-proof record for review discussions.
- File attachments use the platform's existing document management with tenant-scoped storage.
- Manager comments on progress updates are lightweight (max 500 chars) to encourage quick feedback.

## 11. Test Hints
- Verify tenant isolation: progress updates in Tenant A are not visible in Tenant B.
- Test progress update flow: add update, verify progress bar changes, verify manager notification received.
- Test update history: add 5 updates to a goal, verify chronological timeline rendering.
- Test stale goal detection: create a goal with no updates for 15 days, verify nudge notification.
- Test "Blocked" status: set a goal to blocked, verify manager and HR are notified.
- Test append-only: attempt to modify or delete a progress update via API, confirm rejection.
- Test manager comment thread: add comments, verify conversation displays correctly.
- Test mobile at 360px: add a progress update using the mobile interface.
- Test weighted overall completion: set 3 goals with different weights and progress, verify calculation.
