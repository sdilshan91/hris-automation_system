---
id: US-LV-004
module: Leave Management
priority: Must Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-004: Manager Views Pending Leave Queue with Balance Inline

## 1. Description
**As a** Manager,
**I want to** view a queue of pending leave requests from my team members with each employee's leave balance displayed inline,
**So that** I can make informed approval decisions quickly without navigating to separate screens.

## 2. Preconditions
- Manager is authenticated and has `Leave.Approve.Team` permission.
- Manager has direct reports assigned via the Core HR reporting structure (manager_employee_id).
- At least one pending leave request exists from a team member.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Manager navigates to the Leave Approvals page | The page loads | A list of all pending leave requests from their direct reports is displayed, sorted by requested date (oldest first), with employee name, leave type, dates, days, reason, and current balance for that leave type shown inline |
| AC-2 | Manager has 50+ pending requests | They open the approvals page | The list is paginated (default 20 per page) with server-side pagination; total count is shown |
| AC-3 | Manager filters the queue | They apply filters by leave type, employee, or date range | The list updates to show only matching requests |
| AC-4 | Manager clicks on a leave request | The detail panel opens | Full request details are shown including: employee photo, leave type with color tag, date range, total days, reason, attachments (downloadable), current balance, leave history summary (last 3 leaves), and team calendar snippet showing who else is off during the requested period |
| AC-5 | A new leave request is submitted by a team member while the manager is viewing the queue | The notification arrives | The queue refreshes (or a banner prompts refresh) to include the new request |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: API endpoint: `GET /api/v1/leaves/pending` — returns pending leave requests scoped to the manager's team (direct reports) within the tenant.
- FR-2: Each result item includes: `requestId`, `employeeName`, `employeePhoto`, `leaveTypeName`, `leaveTypeColor`, `startDate`, `endDate`, `totalDays`, `reason`, `hasAttachments`, `currentBalance`, `requestedAt`.
- FR-3: Server-side filtering by: leave type, employee, date range, and sorting by requested date or start date.
- FR-4: Server-side pagination with `page`, `pageSize`, and `totalCount` in response.
- FR-5: Team conflict check: For each request, indicate how many team members are already approved off during the overlapping period.
- FR-6: Real-time notification via SignalR when a new leave request arrives for the manager's queue.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Pending queue API must respond within 300ms (P95) using the partial index `ix_leave_pending`.
- NFR-2: Leave balances displayed inline must be fetched from Redis cache; fallback to DB on cache miss.
- NFR-3: Data is tenant-isolated via EF Core filters + PostgreSQL RLS; manager scope limited to direct reports only.
- NFR-4: Page must be fully responsive and usable on mobile (360px+).

## 6. Business Rules
- BR-1: Managers can only see leave requests from their direct reports (not skip-level reports, unless multi-level approval is configured).
- BR-2: If multi-level approval is enabled, the queue shows requests at the manager's approval level.
- BR-3: Requests older than 30 days without action should be visually highlighted as overdue.
- BR-4: Leave balance shown is the current real-time balance, not the balance at the time of request.

## 7. Data Requirements
- **Query:** `SELECT lr.*, lt.name, lt.color, e.first_name, e.last_name, e.photo_url FROM leave_request lr JOIN leave_type lt ... JOIN employee e ... WHERE lr.tenant_id = :tenantId AND lr.status = 'Pending' AND e.manager_employee_id = :managerEmployeeId ORDER BY lr.requested_at ASC`
- **Index used:** `ix_leave_pending ON leave_request(tenant_id, start_date) WHERE status = 'Pending'`.
- **Balance source:** Redis cache key `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 8. UI/UX Notes (Notion-like)
- Notion-like database/table view with inline properties; each row is expandable.
- Compact card view on mobile; table view on desktop.
- Color-coded leave type badges for quick visual scanning.
- Balance shown as a pill badge (green if > 50%, yellow if 20-50%, red if < 20% of entitlement).
- Click-to-expand detail panel slides in from the right (Notion page-peek style).
- Overdue requests (>30 days) have a subtle red left-border highlight.
- Quick-action buttons (Approve / Reject) visible on hover (desktop) or swipe (mobile).
- Filter bar at top with chip-based active filters.

## 9. Dependencies
- **US-LV-003**: Leave requests must be submittable by employees.
- **US-LV-001**: Leave types must be configured (for display names and colors).
- **US-LV-002**: Leave balances must be computed and cached.
- **US-CORE-***: Employee reporting structure (manager_employee_id) must be established.
- **SignalR**: For real-time queue updates.
- **Redis**: For cached leave balances.

## 10. Assumptions & Constraints
- "Direct reports" is determined by the `manager_employee_id` field on the employee record.
- The system does not currently support dotted-line reporting for leave approvals.
- SignalR hub is available at `/hubs/notifications` for real-time updates.
- Maximum page size is capped at 50 to prevent excessive data transfer.

## 11. Test Hints
- Test scope: Manager A should only see requests from their direct reports, not from Manager B's team.
- Test pagination: Create 25 pending requests; verify page 1 returns 20 and page 2 returns 5.
- Test filters: Filter by leave type "Sick"; verify only sick leave requests are returned.
- Test balance inline: Verify the balance displayed matches the employee's actual Redis-cached balance.
- Test tenant isolation: Manager in Tenant A must not see requests from Tenant B.
- Test overdue highlight: Create a request 31 days old; verify it is flagged as overdue.
- Test team conflict: Two employees from same team have overlapping approved leave; verify conflict count is displayed on a new overlapping request.
