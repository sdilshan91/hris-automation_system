---
id: US-LV-009
module: Leave Management
priority: Should Have
persona: Manager / Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-LV-009: Team Leave Calendar View

## 1. Description
**As a** Manager or Employee,
**I want to** view a calendar showing my team's approved and pending leaves,
**So that** I can plan my own time off considering team availability and managers can assess team coverage.

## 2. Preconditions
- User is authenticated with an active employee record.
- The user's team/department structure is defined in Core HR.
- Leave requests exist for team members.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Manager navigates to the Team Leave Calendar | The calendar loads | A month-view calendar displays all approved and pending leaves for their direct reports, with each employee represented as a colored bar/block on the relevant dates |
| AC-2 | Employee navigates to the Team Leave Calendar | The calendar loads | They can see approved leaves (not pending) for members of their own department, providing team awareness without exposing pending request details |
| AC-3 | Manager switches to week view | They toggle the view mode | The calendar shows a detailed week view with employee names on the Y-axis and days on the X-axis (Gantt-like) |
| AC-4 | User views the calendar on a mobile device | The page renders on a 360px screen | The calendar adapts to a compact list view grouped by date, showing employee name, leave type, and status |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: API endpoint: `GET /api/v1/leaves/team-calendar?from={date}&to={date}` — returns leave data scoped to the user's team/department.
- FR-2: Manager view: Shows both approved and pending leaves for direct reports.
- FR-3: Employee view: Shows only approved leaves for department members (no pending).
- FR-4: Response includes: `employeeId`, `employeeName`, `leaveTypeName`, `color`, `startDate`, `endDate`, `status`, `totalDays`.
- FR-5: Calendar views supported: month, week, and list.
- FR-6: Filter by employee, leave type, or status (manager only for status filter).
- FR-7: Public holidays displayed as background highlights on the calendar.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Calendar API for a month range must respond within 300ms (P95).
- NFR-2: All data tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-3: Employee-level access control: employees see department-level approved leaves; managers see direct-report approved + pending.
- NFR-4: Calendar must render smoothly with up to 50 team members and 200 leave entries in view.

## 6. Business Rules
- BR-1: Employees can only see approved leaves of their department colleagues; they cannot see pending requests or leave types (just "on leave").
- BR-2: Managers see full details (leave type, status) for their direct reports only.
- BR-3: HR Officers with `Leave.ViewAll` permission can see the entire organization's leave calendar.
- BR-4: Cancelled leaves are not shown on the calendar.
- BR-5: Half-day leaves are visually differentiated (half-block or AM/PM indicator).

## 7. Data Requirements
- **Query:** Aggregation from `leave_request` joined with `employee` and `leave_type`, filtered by department/manager and date range.
- **Index leveraged:** `leave_request(tenant_id, employee_id, status, start_date)`.

## 8. UI/UX Notes (Notion-like)
- Month view: Clean grid with color-coded leave blocks per employee; hover reveals tooltip with details.
- Week view: Gantt-style horizontal bars with employee names as row labels.
- List view: Chronological list grouped by date with employee cards.
- Holiday dates shown as light gray columns/rows.
- Today's date highlighted with a subtle accent indicator.
- View toggle: Segmented control (Month | Week | List).
- Notion-style filter bar with chip-based active filters.
- Color legend for leave types displayed at the top.
- Mobile: Default to list view; month/week accessible via toggle but optimized for touch.

## 9. Dependencies
- **US-LV-003**: Leave requests must exist to populate the calendar.
- **US-LV-007**: Holiday calendar data for background display.
- **US-CORE-***: Department and reporting structure for scope filtering.

## 10. Assumptions & Constraints
- The calendar component will use a free/open-source Angular calendar library (e.g., angular-calendar or custom implementation with Angular Material).
- The calendar does not support drag-and-drop leave creation (leave is applied via the dedicated form in US-LV-003).
- Department membership is determined by the employee's current department assignment.

## 11. Test Hints
- Test manager vs employee view: Manager sees pending + approved for directs; employee sees only approved for department.
- Test scope: Manager should not see leaves from other managers' teams.
- Test holiday display: Verify public holidays appear as background highlights.
- Test half-day: Verify half-day leaves render differently from full-day leaves.
- Test mobile: Verify list view renders correctly on 360px viewport.
- Test tenant isolation: Calendar data from Tenant A must not appear in Tenant B.
- Test large team: Load 50 employees with 200 leave entries; verify rendering performance.
