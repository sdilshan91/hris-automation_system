---
id: US-RPT-005
module: Reports & Analytics
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-RPT-005: Dashboard with KPI Widgets

## 1. Description
**As an** HR Officer (or Manager or Employee),
**I want to** see a personalized dashboard with KPI widgets showing key HR metrics at a glance (headcount, open positions, pending leave requests, attendance rate, upcoming birthdays, onboarding progress, etc.),
**So that** I can quickly assess the state of HR operations and take timely action on items that need attention.

## 2. Preconditions
- The user is authenticated and has an active session within their tenant.
- Relevant module data exists (employees, leave, attendance, recruitment, onboarding).
- The user's role determines which widgets are visible (HR sees all, Manager sees team, Employee sees self).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer logs in | They land on the dashboard | The dashboard displays role-appropriate KPI widgets: Total Headcount, Open Positions, Pending Leave Requests, Today's Attendance Rate, Upcoming Birthdays/Anniversaries, Recent Joiners, Onboarding In Progress, Turnover Rate (current quarter). Each widget is a card with a numeric value, trend indicator (up/down arrow with percentage change), and a mini chart (sparkline). |
| AC-2 | A Manager logs in | They view their dashboard | The dashboard shows team-scoped widgets: Team Size, Team Attendance Today, Pending Approvals (leave + attendance), Team Leave Calendar (mini calendar), Upcoming Team Reviews, and a Quick Actions panel (approve/reject). |
| AC-3 | An Employee logs in | They view their dashboard | The dashboard shows personal widgets: Leave Balance Summary (donut chart), Attendance This Month (progress bar), Onboarding Progress (if active), Upcoming Holidays, Recent Payslips link, and Pending Actions (tasks, forms to fill). |
| AC-4 | HR Officer clicks on a KPI widget (e.g., "Pending Leave Requests: 12") | They interact with the widget | They are navigated to the relevant module page with a pre-applied filter (e.g., Leave Requests page filtered to "Pending" status). |
| AC-5 | Dashboard data is loaded for a user in Tenant A | A user in Tenant B views their dashboard | Each dashboard shows only their own tenant's data. All widget API calls are tenant-scoped via RLS and EF Core filters. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide role-based dashboards: HR Dashboard, Manager Dashboard, Employee Dashboard.
- FR-2: Each dashboard SHALL consist of configurable KPI widget cards displaying: metric value, label, trend indicator (comparison to previous period), and a mini visualization (sparkline, donut, progress bar).
- FR-3: Widget data SHALL be fetched from dedicated dashboard API endpoints that aggregate data from multiple modules.
- FR-4: The system SHALL cache widget data in Redis (TTL 2-5 minutes) keyed by `t:{tenantId}:dashboard:{role}:{widgetKey}` to minimize database queries.
- FR-5: The system SHALL support widget click-through navigation to the corresponding module with contextual filters pre-applied.
- FR-6: The system SHALL provide an "Announcements" or "Activity Feed" widget showing recent tenant-wide announcements and the user's recent activity.
- FR-7: The system SHALL provide a "Quick Actions" panel for Managers (approve/reject actions) and Employees (apply leave, clock in) directly from the dashboard.
- FR-8: All dashboard data SHALL be scoped by `tenant_id` from the session context and by the user's role-based permissions.
- FR-9: The system SHALL refresh dashboard data automatically every 5 minutes (configurable) or on user demand via a "Refresh" button.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Dashboard page load time SHALL be <= 2 seconds (P95), loading all visible widgets.
- NFR-2: Individual widget API calls SHALL complete within 500 ms (P95).
- NFR-3: All dashboard data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-4: The dashboard SHALL be fully responsive from 360px to 4K resolution; widget grid adjusts from 1 column (mobile) to 4 columns (desktop).
- NFR-5: The dashboard SHALL meet WCAG 2.1 AA accessibility standards (widget cards with ARIA landmarks, contrast ratios, screen-reader friendly metric values).
- NFR-6: Charts and sparklines SHALL use Chart.js / ngx-charts with lazy rendering (render only visible widgets).

## 6. Business Rules
- BR-1: Widget visibility is role-based: HR sees organizational widgets, Managers see team widgets, Employees see personal widgets.
- BR-2: Trend indicators compare the current period to the same-length previous period (e.g., this month vs. last month).
- BR-3: "Pending Approvals" counts only items assigned to the logged-in user (not all pending items in the tenant).
- BR-4: "Upcoming Birthdays/Anniversaries" shows events within the next 7 days.
- BR-5: Dashboard widgets respect module enablement: if the Payroll module is disabled for the tenant, payroll-related widgets are hidden.
- BR-6: The "Quick Actions" panel surfaces the top 5 most urgent actionable items.

## 7. Data Requirements
**Widget response (per widget):**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| widget_key | varchar(50) | Yes | e.g., "headcount", "pending_leave" |
| label | varchar(100) | Yes | Display label |
| value | numeric | Yes | Primary metric value |
| previous_value | numeric | No | For trend calculation |
| trend_direction | varchar(10) | No | "up", "down", "flat" |
| trend_percentage | decimal | No | Percentage change |
| chart_data | jsonb | No | Mini chart data (sparkline points, donut segments) |
| link_url | varchar(200) | No | Click-through URL |
| link_filters | jsonb | No | Pre-applied filters for click-through |

**Output:** Array of widget responses for the user's role-based dashboard.

## 8. UI/UX Notes
- Dashboard layout: responsive CSS grid (Tailwind) with widget cards.
  - Desktop (>= 1280px): 4-column grid
  - Tablet (768px-1279px): 2-column grid
  - Mobile (< 768px): single-column stack
- Widget cards: white background, subtle shadow (`shadow-sm`), rounded corners (`rounded-xl`), padding, and a hover effect (slight elevation).
- KPI value: large font (text-3xl), bold. Trend arrow: green up-arrow for positive trends (headcount growth), red up-arrow for negative trends (turnover increase).
- Sparklines: thin line charts within the card, rendered with Chart.js in a compact size (120px x 40px).
- Donut charts: for leave balance, showing consumed vs. remaining.
- Quick Actions: floating action bar at the bottom (mobile) or a pinned card (desktop) with icon buttons.
- Welcome greeting: "Good morning, [First Name]" with the current date.
- Skeleton loaders for each widget card during data fetching.
- Smooth fade-in animations as widgets load (staggered, 100ms delay between cards).

## 9. Dependencies
- US-CHR-001: Employee data for headcount and demographics widgets.
- Leave Management module: Leave balance and pending request data.
- Attendance module: Attendance rate data.
- Recruitment module: Open positions data.
- US-ONB-002: Onboarding progress data.
- US-NTF-001: Activity feed may include recent notifications.
- Authentication module: Role-based widget visibility.
- Redis: For widget data caching.

## 10. Assumptions & Constraints
- Dashboard is the default landing page after login for all roles.
- Widget configuration (which widgets appear for which role) is hard-coded in Phase 1; drag-and-drop customization is Phase 2.
- Chart.js or ngx-charts (free/open-source) is used for sparklines and mini charts.
- Widget APIs aggregate data across modules; they are optimized read queries, not real-time computations.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **HR dashboard:** Create 50 employees, 5 pending leave requests, 3 open positions; verify widgets show correct values.
- **Manager dashboard:** Assign 8 direct reports to a manager; verify team-scoped widgets show data for those 8 only.
- **Employee dashboard:** Log in as employee; verify personal leave balance, attendance, and onboarding progress widgets.
- **Trend calculation:** Generate headcount data for 2 months; verify trend arrow and percentage change are correct.
- **Click-through:** Click on "Pending Leave Requests" widget; verify navigation to the leave requests page with "Pending" filter applied.
- **Tenant isolation:** View dashboards in Tenant A and B; verify each shows only their own data.
- **Module visibility:** Disable the Payroll module for a tenant; verify payroll-related widgets are hidden.
- **Responsive:** Test dashboard at 360px, 768px, 1280px, and 1920px; verify grid layout adjusts correctly.
- **Performance:** Load dashboard with 10 widgets; verify total load time <= 2 seconds.
- **Accessibility:** Verify widget cards have ARIA landmarks; screen reader announces metric values and labels.
- **Cache:** Load dashboard twice; verify second load hits Redis cache (faster response, verified via response headers or timing).
