---
id: US-LV-012
module: Leave Management
priority: Should Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-012: Leave Reports and Analytics for HR

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** generate and view leave reports and analytics including leave utilization, absenteeism trends, balance summaries, and department-wise comparisons,
**So that** I can make data-driven decisions about leave policies, staffing, and workforce planning.

## 2. Preconditions
- User is authenticated with `Leave.Reports` or `HR.Officer` permission.
- Leave data (requests, balances, ledger entries) exists for the tenant.
- At least one completed leave cycle (month) of data is available.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer navigates to Leave Reports | They select "Leave Balance Summary" report | A table is displayed showing all employees with their current balance for each leave type, filterable by department, job level, and employment type; exportable to CSV/Excel |
| AC-2 | HR Officer selects "Leave Utilization Report" | They specify a date range and department | A report shows total leaves taken by type, average utilization percentage, and a breakdown by department with bar/pie charts |
| AC-3 | HR Officer selects "Absenteeism Report" | They specify a date range | A report shows employees with the highest absenteeism (unplanned leave + LOP), trend lines over the selected period, and flagged employees exceeding configurable thresholds |
| AC-4 | HR Officer selects "Leave Trend Analysis" | They view the dashboard | Line charts showing monthly leave trends by type over the past 12 months, with year-over-year comparison capability |
| AC-5 | HR Officer exports a report | They click the Export button and select CSV or Excel format | The report is generated and downloaded; for large datasets (>5,000 rows), the export is processed as a Hangfire background job and the user is notified when ready |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: Pre-built reports: Leave Balance Summary, Leave Utilization, Absenteeism, Leave Trend Analysis, Carry-Forward Summary, LOP Summary, Department Leave Calendar Coverage.
- FR-2: All reports support filters: date range, department, job level, employment type, leave type, employee search.
- FR-3: All reports support sorting and pagination (server-side).
- FR-4: Export to CSV and Excel (XLSX) using a free open-source library (e.g., ClosedXML for .NET).
- FR-5: Large exports (>5,000 rows) processed via Hangfire background job; file stored in tenant-scoped blob storage; notification sent when ready.
- FR-6: API endpoints: `GET /api/v1/leaves/reports/{reportType}` with query parameters for filters and pagination.
- FR-7: Chart data API: `GET /api/v1/leaves/analytics/{chartType}` returning aggregated data suitable for charting.
- FR-8: Report queries use PostgreSQL read replicas where available for performance.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Report API for datasets up to 1,000 rows must respond within 2 seconds (P95).
- NFR-2: Export generation for up to 5,000 rows must complete within 10 seconds (synchronous); larger exports deferred to background.
- NFR-3: All report data tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-4: Charts must be rendered client-side using a free open-source library (e.g., ngx-charts, Chart.js via ng2-charts).
- NFR-5: Reports must be accessible and printable (print-friendly CSS).

## 6. Business Rules
- BR-1: Reports only show data for the current tenant; no cross-tenant aggregation (system admin has separate cross-tenant reports).
- BR-2: Employee-level data in reports respects role-based access: HR sees all; managers see their team; employees see only their own data.
- BR-3: Leave balance in reports reflects real-time computed values (from Redis cache or DB).
- BR-4: Absenteeism threshold for flagging is tenant-configurable (default: 3+ unplanned leaves per month).
- BR-5: Reports for previous leave years are available (historical data retained per retention policy: 7 years).

## 7. Data Requirements
- **Source tables:** `leave_request`, `leave_ledger`, `leave_type`, `employee`, `department`, `job_title`.
- **Aggregations:** GROUP BY department, leave type, month; SUM of days; COUNT of requests; AVG utilization.
- **Heavy queries:** Use PostgreSQL views or materialized views for complex aggregations; leverage read replicas.
- **Export storage:** `{tenantId}/reports/leave/{reportId}.xlsx` in blob storage for background exports.

## 8. UI/UX Notes (Notion-like)
- Reports landing page: Notion-like grid of report cards with icons, descriptions, and "last generated" timestamps.
- Each report opens in a full-page view with a filter sidebar (collapsible) and results area.
- Charts: Clean, minimal chart styles consistent with Notion/Linear aesthetics; muted colors, clear labels.
- Table results: Notion-like database view with sortable columns, filterable headers, and inline search.
- Export button in top-right with dropdown: CSV, Excel. Progress indicator for background exports.
- Print button generates a clean print-friendly layout.
- Mobile: Reports accessible in table/list view; charts simplified (no hover tooltips, tap for details).
- Dashboard widgets: Key leave metrics (total utilization %, top leave type, absenteeism rate) as summary cards at the top.

## 9. Dependencies
- **US-LV-001 through US-LV-011**: All leave management features feed data into reports.
- **US-CORE-***: Employee, department, and job level data for filtering and grouping.
- **Hangfire**: For background export processing.
- **Blob Storage**: For storing generated export files.
- **PostgreSQL Read Replica**: For offloading heavy report queries (when available).

## 10. Assumptions & Constraints
- Custom report builder (drag-and-drop) is out of scope for Phase 1; only pre-built reports are delivered.
- Charting library must be free and open-source.
- Materialized views for report aggregations are refreshed daily via Hangfire (or on-demand).
- Export files are retained for 30 days in blob storage and then auto-purged.

## 11. Test Hints
- Test balance summary: Generate report; verify balances match individual employee dashboard values.
- Test utilization: Department with 10 employees, 200 total entitlement days, 80 used; verify utilization = 40%.
- Test absenteeism flag: Employee with 4 unplanned leaves in a month (threshold = 3); verify flagged.
- Test export CSV: Generate a 100-row report and export; verify CSV file contains correct headers and data.
- Test large export: Generate a 6,000-row report export; verify it is processed as a background job and user is notified.
- Test filters: Filter by department "Engineering"; verify only engineering employees appear.
- Test tenant isolation: HR in Tenant A must not see any data from Tenant B in reports.
- Test role access: Manager sees only their team's data; employee sees only their own.
