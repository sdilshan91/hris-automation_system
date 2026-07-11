---
id: US-RPT-001
module: Reports & Analytics
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-RPT-001: Pre-Built HR Reports (Headcount, Turnover, Demographics)

## 1. Description
**As an** HR Officer,
**I want to** generate pre-built HR reports covering headcount, employee turnover, demographics (age, gender, department distribution), and joiners/leavers analysis with filters for date range, department, and location,
**So that** I can make data-driven decisions about workforce planning, identify trends, and provide management with accurate HR metrics.

## 2. Preconditions
- The HR Officer is authenticated and has `Reports.View.All` permission within their tenant.
- Employee data exists in the tenant (Core HR module is populated).
- The Reporting module is enabled for the tenant's subscription plan.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer navigates to Reports > HR Reports | They view the report catalog | A list of pre-built report types is displayed: Headcount Summary, Employee Turnover, Demographics, Joiners & Leavers, Department Distribution, Employment Type Breakdown. Each report has a description, icon, and "Generate" button. |
| AC-2 | HR Officer selects "Headcount Summary" and applies filters (date = current month, department = "Engineering") | They click "Generate" | A report is rendered showing: total headcount, active vs. inactive count, breakdown by employment type (full-time, part-time, contract, intern), and a bar chart showing headcount by sub-department. All data is scoped to the tenant. |
| AC-3 | HR Officer selects "Employee Turnover" for the last 12 months | The report is generated | The report shows: total separations, voluntary vs. involuntary turnover count and percentage, monthly turnover trend (line chart), turnover by department (horizontal bar chart), and average tenure of departed employees. |
| AC-4 | HR Officer selects "Demographics" | The report is generated | The report shows: gender distribution (pie chart), age distribution (histogram with 5-year buckets), department-wise headcount (stacked bar chart), location distribution (map or bar chart), and diversity metrics. |
| AC-5 | HR Officer generates a report in Tenant A | A user from Tenant B queries the same report | Tenant B's report shows only Tenant B data; no cross-tenant data leakage occurs due to RLS and EF Core filters. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide the following pre-built reports: Headcount Summary, Employee Turnover, Demographics, Joiners & Leavers, Department Distribution, Employment Type Breakdown.
- FR-2: Each report SHALL support filters: date range, department (multi-select with hierarchy), location, employment type, and employee status.
- FR-3: Reports SHALL be rendered with interactive charts using Chart.js or ngx-charts: bar charts, line charts, pie/donut charts, and histograms.
- FR-4: Reports SHALL include both visual charts and tabular data views, togglable by the user.
- FR-5: The system SHALL cache report results in Redis (keyed by `t:{tenantId}:report:{name}:{paramsHash}`, TTL 5-15 minutes) for repeat queries.
- FR-6: Reports SHALL use PostgreSQL views or materialized views for complex aggregations to minimize query time.
- FR-7: The system SHALL set `tenant_id` in the query context to ensure all report data is tenant-scoped.
- FR-8: The system SHALL provide a "Refresh" option to bypass the cache and regenerate the report.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Report generation SHALL complete within 3 seconds (P95) for tenants with up to 5,000 employees.
- NFR-2: All report data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Chart rendering SHALL complete within 1 second on the client side for datasets up to 10,000 data points.
- NFR-4: The report UI SHALL be fully responsive from 360px to 4K resolution; charts SHALL resize dynamically.
- NFR-5: The report UI SHALL meet WCAG 2.1 AA accessibility standards (charts with alt text, data tables as alternatives).
- NFR-6: Heavy reports SHALL use read replicas if configured (PostgreSQL streaming replication).

## 6. Business Rules
- BR-1: Report data is read-only; reports do not modify any business data.
- BR-2: HR Officers with `Reports.View.All` see the full tenant's data; Managers with `Reports.View.Team` see only their direct reports' data.
- BR-3: Turnover rate is calculated as: (Separations in Period / Average Headcount in Period) x 100.
- BR-4: "Active" employees include those with status `active` or `probation`; `terminated`, `resigned`, and `contract_ended` are considered separated.
- BR-5: Demographic data (age, gender) is calculated at the report date, not the current date, for historical accuracy.
- BR-6: Reports respect the tenant's fiscal year start for annual comparisons.

## 7. Data Requirements
**Filter inputs:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| report_type | varchar(50) | Yes | Must be a valid pre-built report key |
| date_from | date | No | Defaults to start of current month |
| date_to | date | No | Defaults to today |
| department_ids | uuid[] | No | Must exist in tenant |
| location_ids | uuid[] | No | Must exist in tenant |
| employment_types | varchar(30)[] | No | Valid employment type values |
| employee_status | varchar(20)[] | No | Valid status values |

**Output:** Report object with metadata (title, generated_at, filters applied), chart data (series, labels, values), and tabular data (rows with columns).

## 8. UI/UX Notes
- Report catalog: card grid layout with report type icon, title, description, and "Generate" button.
- Report view: filter bar at the top (collapsible), charts in the main area, data table below.
- Charts: interactive with hover tooltips showing exact values. Click on a chart segment to drill down (e.g., click a department bar to see employees in that department).
- Toggle between chart view and table view via a view switcher (icons).
- Print-friendly layout available via a "Print" button (hides navigation, optimizes chart sizes).
- On mobile (< 768px): charts stack vertically in single-column layout; tables become horizontally scrollable.
- Loading state: skeleton loader for charts and tables during generation.
- Color palette: use the tenant's brand color as the primary chart color, with a harmonious generated palette for multi-series charts.

## 9. Dependencies
- US-CHR-001: Employee records must exist for HR reports.
- US-CHR-004: Department data for department-based filtering and grouping.
- US-RPT-004: Export functionality for reports.
- Authentication module: User must have `Reports.View` permission.
- Redis: For report result caching.

## 10. Assumptions & Constraints
- Chart.js or ngx-charts (free/open-source) is used for all chart visualizations.
- Complex report queries use PostgreSQL views or materialized views refreshed on a schedule (Hangfire).
- Read replicas are optional; if not configured, reports query the primary database.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Generate a Headcount Summary for the current month; verify total matches the count of active employees in the tenant.
- **Turnover calculation:** Create 100 employees, terminate 10; generate turnover report; verify rate = 10%.
- **Demographics:** Create employees with varied ages and genders; verify pie and histogram charts reflect the data accurately.
- **Department filter:** Filter by a specific department; verify only employees in that department are included.
- **Tenant isolation:** Generate the same report type in Tenant A and B; verify each shows only their own data.
- **Caching:** Generate a report, then generate it again with same filters; verify the second call is served from Redis cache (faster response).
- **Manager scope:** Log in as a Manager; generate a report; verify only direct reports' data is included.
- **Responsive:** Test chart rendering at 360px and 1920px; verify charts resize dynamically.
- **Accessibility:** Verify charts have alt text and a data table alternative is available for screen readers.
