---
id: US-ATT-010
module: Attendance
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-010: Attendance Dashboard and Reports for HR

## 1. Description
**As an** HR Officer,
**I want to** view a real-time attendance dashboard and generate detailed attendance reports,
**So that** I can monitor workforce attendance patterns, identify issues proactively, and make data-driven HR decisions.

## 2. Preconditions
- HR Officer must be authenticated with a valid JWT session.
- HR Officer must have the `Attendance.Read.All` and `Reports.View.All` permissions.
- The Attendance module must be enabled for the tenant.
- Attendance data must exist for the reporting period.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employees have clocked in for the current day | HR Officer opens the attendance dashboard | The system displays a real-time overview: total employees expected today, clocked-in count, not-yet-clocked-in count, on-leave count, and a live attendance percentage |
| AC-2 | HR Officer wants to view a daily attendance board | HR Officer clicks "Live Board" | The system shows a real-time list of all employees with their current status (Clocked In, Not Clocked In, On Leave, Holiday) updated via SignalR |
| AC-3 | HR Officer wants a departmental attendance comparison | HR Officer selects the departmental report | The system displays attendance rates by department as a bar chart with drill-down capability |
| AC-4 | HR Officer generates a custom date-range attendance report | HR Officer selects a date range, optional department/location filters, and clicks "Generate" | The system produces a detailed report with daily attendance records for all matching employees |
| AC-5 | HR Officer wants to identify attendance trends | HR Officer navigates to the trends section | The system displays line charts showing monthly attendance rate, average late arrivals, and overtime trends over the past 12 months |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide a real-time attendance dashboard showing today's attendance KPIs: expected headcount, clocked-in, pending clock-in, on-leave, absent, attendance percentage.
- FR-2: The live attendance board shall update in real-time using SignalR when employees clock in or out.
- FR-3: The system shall provide pre-built reports: Daily Attendance, Weekly Summary, Monthly Summary, Departmental Comparison, Late Arrival Report, Overtime Report, Absenteeism Report.
- FR-4: The system shall support custom date-range reports with filters: department, location, shift, employee status, and specific employees.
- FR-5: All reports shall support export in CSV, Excel (.xlsx), and PDF formats.
- FR-6: The system shall provide trend analytics: monthly attendance rate trend (12 months), average late arrivals per month, overtime hours trend, absenteeism rate trend.
- FR-7: The dashboard KPIs shall be cached in Redis and refreshed on each clock-in/out event.
- FR-8: The system shall support scheduled report delivery: HR can configure reports to be auto-generated and emailed daily/weekly/monthly via Hangfire.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The dashboard must load within 2 seconds at P95, leveraging Redis-cached KPIs.
- NFR-2: The live attendance board must reflect clock-in/out events within 3 seconds via SignalR.
- NFR-3: Report generation for up to 5,000 employees and 30 days must complete within 15 seconds.
- NFR-4: PostgreSQL RLS must enforce tenant isolation on all dashboard and report data.
- NFR-5: Dashboard and reports must be fully responsive and usable on tablet and mobile screens.
- NFR-6: Scheduled reports must be processed by Hangfire without impacting production performance (run during off-peak hours).

## 6. Business Rules
- BR-1: "Expected headcount" for today = total active employees minus employees on full-day approved leave minus employees at locations where today is a public holiday.
- BR-2: "Attendance percentage" = (clocked_in_count / expected_headcount) * 100, updated in real-time.
- BR-3: The live board only shows employees the HR Officer has permission to view (all employees for `Attendance.Read.All`).
- BR-4: Managers viewing the dashboard see only their team (scoped by `Attendance.Read.Team` permission).
- BR-5: Trend data is calculated from the `attendance_monthly_summary` table for performance.
- BR-6: Scheduled reports respect the recipient's timezone for delivery timing.
- BR-7: Reports older than the tenant's data retention period (default 7 years per technical document) are archived.

## 7. Data Requirements
**Dashboard KPIs (Redis cached):**
| Key | Type | Notes |
|-----|------|-------|
| att_dashboard:{tenant_id}:{date}:expected | integer | Expected headcount |
| att_dashboard:{tenant_id}:{date}:clocked_in | integer | Current clocked-in count |
| att_dashboard:{tenant_id}:{date}:on_leave | integer | On leave count |
| att_dashboard:{tenant_id}:{date}:absent | integer | Not clocked in, not on leave |
| att_dashboard:{tenant_id}:{date}:attendance_pct | decimal | Live percentage |

**scheduled_report_config table:**
| Field | Type | Notes |
|-------|------|-------|
| config_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| report_type | varchar(50) | Pre-built report type |
| frequency | varchar(10) | 'DAILY', 'WEEKLY', 'MONTHLY' |
| filters | jsonb | Saved filter configuration |
| recipients | UUID[] | Array of user IDs |
| delivery_time | time | Time of day to send |
| format | varchar(10) | 'CSV', 'XLSX', 'PDF' |
| is_active | boolean | |
| created_by | UUID | HR Officer who configured |
| created_at / updated_at | timestamptz | Audit |

## 8. UI/UX Notes (Notion-like)
- The dashboard should be a Notion-style page with widget cards for each KPI (large numbers with subtle labels).
- Use a donut/pie chart for today's attendance breakdown (Clocked In / On Leave / Absent / Pending).
- The live attendance board should be a real-time table with employee avatars, names, status pills, and clock-in times. Use a subtle animation (e.g., row highlight) when a new clock-in occurs.
- Department comparison should use horizontal bar charts with color-coded attendance rates (green > 90%, amber 80-90%, red < 80%).
- Trend charts should use smooth line charts with tooltips showing exact values on hover.
- Report generation should use a Notion-style command palette or sidebar where users select report type, date range, and filters.
- Export buttons should be grouped in a dropdown: "Export as CSV / Excel / PDF."
- Scheduled report setup should be a simple form: select report, frequency, filters, recipients, delivery time.
- Mobile: dashboard should stack KPI cards vertically; charts should be swipeable; the live board should use a card layout.
- Use skeleton loading screens for dashboard components while data loads.

## 9. Dependencies
- US-ATT-001 / US-ATT-002: Clock-in/out events drive real-time dashboard updates.
- US-ATT-007: Monthly summary provides data for trend analytics and reports.
- US-ATT-008: Late arrival data feeds into the late arrival report.
- US-ATT-006: Overtime data feeds into the overtime report.
- Leave Management module: Leave data for expected headcount calculation.
- Core HR module: Employee, department, and location data for filtering and grouping.
- SignalR infrastructure: Real-time updates for the live attendance board.
- Redis: Caching dashboard KPIs.
- Hangfire: Scheduled report generation and delivery.
- Notification System: Email delivery of scheduled reports.

## 10. Assumptions & Constraints
- The real-time dashboard relies on Redis for KPI caching; if Redis is unavailable, the system falls back to a database query (with degraded performance).
- SignalR is used for the live board; if WebSocket connection fails, the UI falls back to periodic polling (every 30 seconds).
- Pre-built reports cover the most common use cases; a custom report builder is deferred to Phase 2.
- Trend analytics use pre-computed monthly summaries, not raw attendance logs, for performance.
- Multi-tenant RLS ensures dashboard and report data are isolated per tenant.
- The scheduled report feature uses Hangfire's recurring job capability with tenant context injection.
- Charts are rendered client-side using a JavaScript charting library (e.g., Chart.js or ngx-charts).

## 11. Test Hints
- Test real-time dashboard KPIs: clock in multiple employees, verify counts update.
- Test SignalR live board: clock in an employee, verify the board updates in real-time in another browser session.
- Test department comparison: create employees in different departments with varied attendance, verify chart accuracy.
- Test custom date-range report: select a 2-week range with department filter, verify correct data.
- Test export in all formats: generate a report with 100 employees, export as CSV/Excel/PDF, verify content.
- Test trend analytics: verify 12-month trend chart shows correct monthly attendance rates.
- Test scheduled report: configure a daily report, trigger the Hangfire job, verify email delivery.
- Test permission scoping: log in as a Manager, verify dashboard shows only team data.
- Test Redis fallback: disable Redis, verify dashboard still loads (with slower performance).
- Test multi-tenant isolation: verify Tenant A's dashboard does not show Tenant B's data.
- Test mobile responsiveness: view dashboard on 360px viewport, verify all components are usable.
