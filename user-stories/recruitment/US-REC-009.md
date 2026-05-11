---
id: US-REC-009
module: Recruitment
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-009: Recruitment Dashboard and Analytics

## 1. Description
**As an** HR Officer,
**I want to** view a recruitment dashboard with key metrics such as time-to-hire, applicants per vacancy, pipeline funnel conversion rates, and source effectiveness,
**So that** I can identify bottlenecks, measure recruiter performance, and optimize the hiring process.

## 2. Preconditions
- The user is authenticated and has `Recruitment.Read.All` and `Reports.View.All` (or `Reports.View.Department`) permissions within the resolved tenant.
- At least one vacancy with applicant data exists within the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The tenant has recruitment data (vacancies, applicants, interviews, offers, hires) | The HR Officer navigates to the Recruitment Dashboard | A dashboard is displayed with KPI cards showing: total open vacancies, total applicants (current period), average time-to-hire, offer acceptance rate, and a recruitment funnel chart |
| AC-2 | The dashboard is loaded | The HR Officer selects a date range filter (e.g., last 30 days, last quarter, custom range) | All metrics and charts update to reflect only data within the selected period |
| AC-3 | The dashboard shows a recruitment funnel | The HR Officer views the funnel | A visual funnel/bar chart shows the conversion rates between pipeline stages (e.g., Applied: 100, Screening: 60, Interview: 30, Offer: 10, Hired: 5) with percentage drop-off between each stage |
| AC-4 | The dashboard shows source effectiveness | The HR Officer views the source breakdown | A chart displays applicants by source (public careers page, internal, referral) with hire conversion rate per source |
| AC-5 | An HR Officer in Tenant A views the dashboard | The system queries analytics data | Only Tenant A's recruitment data is aggregated; PostgreSQL RLS and tenant-scoped queries ensure no cross-tenant data leakage |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL display a recruitment dashboard with the following KPI cards: Open Vacancies (count), Total Applicants (period), Hires (period), Average Time-to-Hire (days, from application to hire), Offer Acceptance Rate (%), Offers Pending (count).
- FR-2: The system SHALL display a recruitment funnel chart showing applicant counts at each pipeline stage with conversion rate percentages between stages.
- FR-3: The system SHALL display a source effectiveness chart (bar or pie chart) showing applicants by source and hire conversion rate per source.
- FR-4: The system SHALL display a time-to-hire trend chart (line chart) showing average time-to-hire over the selected period (weekly or monthly data points).
- FR-5: The system SHALL display a vacancy status summary: count of vacancies by status (Draft, Open, On Hold, Closed) as a horizontal stacked bar or donut chart.
- FR-6: The system SHALL support date range filtering (preset: last 7 days, last 30 days, last quarter, last year, custom range) applied globally to all dashboard metrics.
- FR-7: The system SHALL support filtering by department and vacancy to drill down into specific areas.
- FR-8: The system SHALL provide export functionality for dashboard data: CSV and Excel (.xlsx via ClosedXML) for tabular data, and PDF (via QuestPDF) for the full dashboard view.
- FR-9: The system SHALL display a "Recent Activity" feed showing the latest recruitment events (new applicant, stage change, offer sent, hire) with timestamps.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The dashboard SHALL load within 2.5 seconds (P95) for tenants with up to 10,000 applicant records.
- NFR-2: All analytics queries SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: Analytics data MAY be pre-aggregated using materialized views or cached in Redis (with tenant-scoped cache keys) for performance.
- NFR-4: The dashboard SHALL be fully responsive (360px to 4K); charts SHALL resize and reflow for mobile viewports.
- NFR-5: Export generation for large datasets SHALL be asynchronous (queued via Hangfire) with a download notification when complete.

## 6. Business Rules
- BR-1: Time-to-hire is calculated as the number of calendar days from the applicant's `applied_at` date to the date they entered the "Hired" stage.
- BR-2: Offer acceptance rate = (Accepted Offers / Total Sent Offers) * 100 for the selected period.
- BR-3: Funnel conversion rate = (Count at Stage N+1 / Count at Stage N) * 100 for each adjacent stage pair.
- BR-4: Dashboard data refreshes on page load; there is no real-time streaming in Phase 1.
- BR-5: Only users with `Reports.View.All` see the full dashboard; users with `Reports.View.Department` see metrics filtered to vacancies in their department.
- BR-6: Source categories are: Public Careers Page, Internal Application, Referral, Manual Entry. Tenants may add custom sources.

## 7. Data Requirements
- **Input:** Tenant ID (from context), date range, department filter, vacancy filter.
- **Output:** Dashboard DTO containing: KPI values (numeric), funnel data (stage name, count, conversion rate), source data (source name, applicant count, hire count, conversion rate), time-to-hire trend data (date, average days), vacancy status counts, recent activity events.
- **Storage:** Queries aggregate from `applicant`, `applicant_stage_history`, `vacancy`, `offer`, `interview` tables. Optional: materialized view `mv_recruitment_analytics` refreshed periodically by Hangfire.

## 8. UI/UX Notes
- **Layout:** Top row of KPI cards (large numbers with trend indicators: up/down arrows comparing to previous period). Below: 2-column grid of charts on desktop; single column on mobile.
- **KPI cards:** Notion-like cards with subtle shadows, large primary metric, small label, and a micro-sparkline or trend arrow.
- **Funnel chart:** Horizontal funnel or vertical bar chart with stage labels, counts, and conversion rate percentages between stages.
- **Source chart:** Horizontal bar chart or donut chart with legend.
- **Time-to-hire trend:** Line chart with data points, hover tooltips showing exact values.
- **Date range filter:** Pill-style preset buttons (7d, 30d, 90d, 1y) plus a custom date range picker.
- **Recent activity feed:** Vertical list with icons, event description, relative timestamps ("2 hours ago"), and links to the relevant applicant or vacancy.
- **Charts library:** Use a free open-source library (ngx-charts or Chart.js with ng2-charts).
- **Mobile:** Stack all cards and charts vertically; charts use full width; horizontal scroll for wide charts if needed.
- **Empty state:** Show placeholder charts with "No data for the selected period" message.

## 9. Dependencies
- US-REC-001 through US-REC-007 (recruitment data must exist for meaningful analytics).
- Reports & Analytics module (S33) for export formats (CSV via ClosedXML, PDF via QuestPDF).
- Redis caching (S27) for pre-aggregated analytics data.
- Hangfire (S28) for async export generation and optional materialized view refresh.

## 10. Assumptions & Constraints
- The dashboard shows pre-built, fixed reports in Phase 1; a custom report builder is out of scope (S3.2).
- Data freshness: dashboard queries live data or materialized views refreshed at most every 15 minutes.
- Chart rendering is client-side (Angular); the API returns data, not images.
- Department-scoped analytics (for managers) filter by vacancies where the hiring manager is in the user's department.

## 11. Test Hints
- Seed a tenant with known recruitment data (e.g., 100 applicants across stages) and verify all KPI values match expected calculations.
- Test time-to-hire calculation: create an applicant, advance to "Hired", and verify the time-to-hire metric is correct.
- Test offer acceptance rate: send 10 offers, accept 7, verify rate shows 70%.
- Test funnel conversion: 100 Applied, 60 Screening, 30 Interview; verify conversion rates show 60%, 50%.
- Test date range filter: apply "last 7 days" and verify only recent data appears.
- Test department filter: verify a Manager-level user sees only their department's vacancies.
- Test cross-tenant isolation: verify Tenant B's dashboard shows zero data from Tenant A.
- Test export: export dashboard data as CSV and verify the file contains correct data.
- Test responsive layout at 360px: verify charts reflow to single column and are readable.
- Test empty state: view dashboard for a tenant with no recruitment data and verify friendly empty state messages.
