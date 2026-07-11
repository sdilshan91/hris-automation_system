---
id: US-ATT-007
module: Attendance
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-007: Monthly Attendance Summary per Employee

## 1. Description
**As an** HR Officer,
**I want to** view a monthly attendance summary for each employee showing present days, absent days, late arrivals, overtime hours, and leave days,
**So that** I can review attendance patterns and provide accurate data to the payroll module.

## 2. Preconditions
- HR Officer must be authenticated with a valid JWT session.
- HR Officer must have the `Attendance.Read.All` permission.
- The Attendance module must be enabled for the tenant.
- Attendance data must exist for the selected month.
- The monthly summary Hangfire job must have run for the selected period (or on-demand generation is triggered).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Attendance data exists for the selected month | HR Officer navigates to the monthly attendance summary page and selects a month | The system displays a table with one row per employee showing: total present days, absent days, late arrivals, early departures, overtime hours, leave days, and total work hours |
| AC-2 | HR Officer wants to drill down into a specific employee's attendance | HR Officer clicks on an employee row | The system shows a day-by-day breakdown for that employee for the selected month, with clock-in/out times, status, and any regularizations |
| AC-3 | The monthly summary has not yet been generated for the current month | HR Officer requests the summary | The system triggers an on-demand calculation via Hangfire and displays a progress indicator until the summary is ready |
| AC-4 | HR Officer wants to export the summary | HR Officer clicks "Export" and selects a format (CSV, Excel, PDF) | The system generates and downloads the file with all summary data for the selected month |
| AC-5 | HR Officer filters the summary by department | HR Officer selects a department from the filter dropdown | The system displays the summary only for employees in the selected department |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide a Hangfire recurring job that runs daily (e.g., 1:00 AM tenant timezone) to compute and cache the attendance summary for the previous day.
- FR-2: The system shall provide a monthly summary aggregation job that runs on the 1st of each month for the previous month.
- FR-3: The monthly summary shall include per employee: total_present_days, total_absent_days, total_late_count, total_early_departure_count, total_work_hours, total_overtime_hours, total_leave_days, total_holidays, loss_of_pay_days.
- FR-4: The system shall allow on-demand summary generation for the current (incomplete) month.
- FR-5: The system shall support filtering by department, location, shift, and employee status.
- FR-6: The system shall support exporting summaries in CSV, Excel (.xlsx via ClosedXML), and PDF (QuestPDF) formats.
- FR-7: Large exports (> 1,000 employees) shall be processed asynchronously via Hangfire with a download link sent via notification.
- FR-8: The daily attendance summary shall be cached in Redis with the key `att_summary:{tenant_id}:{year_month}:{employee_id}`.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The monthly summary page must load within 2.5 seconds at P95 for tenants with up to 5,000 employees (leveraging Redis cache).
- NFR-2: The Hangfire summary job must complete within 10 minutes for 5,000 employees.
- NFR-3: PostgreSQL RLS must enforce tenant isolation; HR in Tenant A cannot see summaries for Tenant B.
- NFR-4: Export file generation for up to 500 employees must complete within 30 seconds.
- NFR-5: The summary data must be accurate to the minute for work hours and overtime.

## 6. Business Rules
- BR-1: A "present day" is defined as a day where the employee has a clock-in record (manual or regularized) and total work hours meet the shift's minimum threshold.
- BR-2: An "absent day" is a scheduled working day with no attendance record and no approved leave.
- BR-3: Loss of Pay (LOP) days are absent days that are not covered by any leave type. LOP feeds into payroll deductions.
- BR-4: Public holidays and weekly offs are excluded from present/absent calculations.
- BR-5: Half-day attendance (where total hours are less than standard but above 50% of standard) is counted as 0.5 present days (if tenant policy supports half-day).
- BR-6: The summary must reconcile with leave data: approved leave days must not be counted as absent.
- BR-7: Regularized attendance (approved) is treated identically to normal attendance in the summary.

## 7. Data Requirements
**attendance_monthly_summary table (materialized/cached):**
| Field | Type | Notes |
|-------|------|-------|
| summary_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| employee_id | UUID | FK |
| year_month | varchar(7) | '2026-05' format |
| total_present_days | decimal(4,1) | Supports half-days |
| total_absent_days | decimal(4,1) | |
| total_late_count | integer | |
| total_early_departure_count | integer | |
| total_work_minutes | integer | Total across the month |
| total_overtime_minutes | integer | Approved overtime only |
| total_leave_days | decimal(4,1) | From leave module |
| total_holidays | integer | Public holidays in the month |
| total_weekly_offs | integer | |
| lop_days | decimal(4,1) | Loss of Pay |
| generated_at | timestamptz | When the summary was last calculated |
| created_at / updated_at | timestamptz | Audit |

## 8. UI/UX Notes (Notion-like)
- The monthly summary should be displayed as a Notion-style database table with sortable and filterable columns.
- Use a month-year picker at the top for navigation (left/right arrows + dropdown).
- Color-code cells: red for absent days > threshold, amber for high late count, green for full attendance.
- Provide an inline sparkline or mini bar chart for each employee showing daily attendance pattern.
- The drill-down view should be a calendar/grid view showing each day's status (present, absent, leave, holiday, weekly off) with color coding.
- Export button should be in the table toolbar with format options (CSV, Excel, PDF).
- Filters (department, location, shift) should be Notion-style filter chips above the table.
- Mobile: use a card layout for each employee with expandable detail section.
- Show a summary banner at the top: total employees, average attendance %, total LOP days.

## 9. Dependencies
- US-ATT-001 / US-ATT-002: Daily clock-in/out records feed into the summary.
- US-ATT-003 / US-ATT-004: Approved regularizations are included in the summary.
- US-ATT-006: Approved overtime hours are included.
- US-ATT-008: Late arrival and early departure counts are included.
- Leave Management module: Leave data for reconciliation.
- Core HR module: Employee, department, and location data for filtering.
- US-ATT-009: The monthly summary is the primary input to the payroll module.
- Hangfire: Background job infrastructure for summary computation.
- Redis: Caching infrastructure for summary data.

## 10. Assumptions & Constraints
- The summary is a computed/materialized view, not a real-time calculation, for performance reasons.
- The Hangfire job uses tenant-scoped queries with RLS to ensure data isolation during batch processing.
- For the current (incomplete) month, on-demand generation computes the summary up to the current date.
- The summary does not include future dates or projections.
- Multi-tenant isolation is enforced at both the application layer (tenant context in Hangfire job) and database layer (RLS).
- The summary reconciliation with leave data assumes the Leave module is operational and leave records are up to date.

## 11. Test Hints
- Test summary generation for a full month with varied attendance (present, absent, late, overtime, leave, holidays).
- Test LOP calculation: employee absent 3 days with no leave, verify lop_days = 3.
- Test half-day counting: employee works 4 hours on an 8-hour shift, verify 0.5 present day (if tenant supports half-day).
- Test leave reconciliation: employee on approved leave, verify not counted as absent.
- Test holiday exclusion: public holiday falls on a weekday, verify not counted as absent or present.
- Test export in all three formats (CSV, Excel, PDF): verify data accuracy and formatting.
- Test async export for large datasets (> 1,000 employees): verify Hangfire job processes and notification is sent.
- Test on-demand generation for current month: verify partial summary is generated.
- Test multi-tenant isolation: verify Tenant A's summary does not include Tenant B's data.
- Test department filter: verify only employees in the selected department are shown.
- Test Redis cache: verify summary is served from cache on subsequent page loads.
