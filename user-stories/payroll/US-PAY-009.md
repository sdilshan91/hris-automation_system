---
id: US-PAY-009
module: Payroll
priority: Should Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-009: Payroll Reports and Analytics

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** generate and view payroll reports and analytics (payroll totals, department-wise breakdowns, statutory summaries, bank advice files, year-end tax statements),
**So that** I can ensure compliance, support financial planning, and facilitate fund disbursement.

## 2. Preconditions
- At least one finalized payroll run exists for the tenant.
- User has `Payroll.*.All` or `Reports.*.All` permission.
- Payroll module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR navigates to Payroll Reports | HR selects "Payroll Summary Report" for May 2026 | The system displays a summary report with total gross, total deductions, total statutory, total net, and employee count; department-wise breakdown is shown in a table and bar chart |
| AC-2 | HR needs to generate a bank advice file | HR selects the finalized payroll run and clicks "Generate Bank Advice" | The system generates a bank advice file (CSV/Excel) with columns: Employee Name, Bank Name, Account Number, IFSC/Branch Code, Net Salary Amount; the file is available for download |
| AC-3 | HR needs a year-end tax statement for all employees | HR selects "Year-End Tax Statements" for fiscal year 2025-2026 | The system generates individual tax statements (PDF) for each employee showing month-wise income, deductions, and total tax paid; these are available for bulk download |
| AC-4 | HR exports a payroll report to Excel | HR clicks "Export" on any payroll report | An Excel file is generated using ClosedXML (per technical doc section 33.4) and downloaded |
| AC-5 | Reports are accessed by Tenant A | The reports only contain data from Tenant A | RLS ensures no cross-tenant data is included in any report; the report engine applies tenant_id filtering at the query level |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide the following out-of-the-box payroll reports:
  - a. Payroll Summary Report (period-wise totals with department breakdown)
  - b. Employee Payroll Register (all employees with component-wise breakdown for a period)
  - c. Department-wise Payroll Summary
  - d. Statutory Deduction Report (tax, EPF, ETF summaries for filing)
  - e. Bank Advice File (for salary disbursement)
  - f. Year-End Tax Statement (per employee, for tax filing)
  - g. Payroll Variance Report (month-over-month comparison)
  - h. CTC Report (current CTC of all employees)
- FR-2: The system SHALL support report export in CSV, Excel (.xlsx via ClosedXML), and PDF (via QuestPDF) formats (per technical doc section 33.4).
- FR-3: The system SHALL support report filtering by: pay period, department, designation, employment type, salary structure, and custom date ranges.
- FR-4: The system SHALL generate large reports asynchronously via Hangfire and notify the user when the report is ready for download.
- FR-5: The system SHALL provide a payroll analytics dashboard with visual charts: monthly payroll trend (line chart), department cost distribution (pie chart), statutory deduction breakdown (stacked bar chart).
- FR-6: Bank advice file format SHALL be configurable per tenant (column order, delimiter, bank-specific formats).
- FR-7: Year-end tax statements SHALL be generated as individual PDFs and available as a bulk ZIP download.
- FR-8: All report queries SHALL be scoped by `tenant_id` via RLS.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Report generation for up to 5,000 employees SHALL complete within 2 minutes for standard reports.
- NFR-2: Year-end tax statement generation (5,000 PDFs) SHALL complete within 15 minutes (async via Hangfire).
- NFR-3: Report data SHALL be served from read replicas or cached aggregations where possible to minimize load on the primary database.
- NFR-4: Export files SHALL be temporarily stored and auto-deleted after 24 hours.
- NFR-5: Test coverage for report generation logic SHALL be >= 85%.
- NFR-6: Dashboard charts SHALL render within 3 seconds using pre-aggregated data.

## 6. Business Rules
- BR-1: Payroll reports are only generated from Finalized payroll runs.
- BR-2: Bank advice files must mask account numbers partially (show last 4 digits only) in the UI preview but include full numbers in the downloaded file.
- BR-3: Year-end tax statements must include cumulative totals across all payroll runs in the fiscal year, including adjustments and arrears.
- BR-4: Payroll variance report must highlight significant changes (> 10% increase/decrease) for individual employees or departments.
- BR-5: Reports must respect the tenant's configured fiscal year start month (e.g., April for India, January for US).
- BR-6: Statutory reports must be formatted according to the tenant's country/jurisdiction requirements for ease of filing.
- BR-7: Terminated employees must be included in historical reports for the periods they were active.

## 7. Data Requirements

**Payroll Summary Report Columns:**
| Field | Description |
|-------|-------------|
| Department | Department name |
| Employee Count | Number of employees |
| Total Basic | Sum of basic component |
| Total Allowances | Sum of earning components (excl. basic) |
| Total Gross | Sum of gross earnings |
| Total Statutory | Sum of statutory deductions |
| Total Other Deductions | Sum of non-statutory deductions |
| Total Net | Sum of net salaries |

**Bank Advice File Columns:**
| Field | Description |
|-------|-------------|
| Employee No | Employee number |
| Employee Name | Full name |
| Bank Name | Bank name |
| Branch Code | IFSC/SWIFT/sort code |
| Account Number | Bank account number |
| Net Amount | Net salary to transfer |
| Narration | Payment reference (e.g., "Salary May 2026") |

**Pre-aggregated table for dashboard (materialized or cache):**
| Column | Type |
|--------|------|
| tenant_id | uuid |
| pay_month | int |
| pay_year | int |
| department_id | uuid |
| total_gross | numeric(18,2) |
| total_deductions | numeric(18,2) |
| total_net | numeric(18,2) |
| employee_count | int |

## 8. UI/UX Notes (Notion-like)
- Reports page: Notion-style sidebar listing available report types with icons; selecting a report shows the configuration panel (filters) and preview area.
- Dashboard: card-based analytics layout with interactive charts (use open-source charting library like ngx-charts or Chart.js).
- Monthly trend chart: line chart with gross, deductions, and net lines; hoverable tooltips showing exact values.
- Department breakdown: horizontal bar chart sorted by total cost descending.
- Export buttons (CSV, Excel, PDF) as icon buttons in the report toolbar.
- Bank advice preview: table view with masked account numbers and a "Download Full File" button.
- Large report generation: show a progress indicator and "We'll notify you when it's ready" message for async reports.
- Mobile: dashboard charts viewable; report generation and download available; detailed tables defer to desktop for readability.

## 9. Dependencies
- **US-PAY-003**: Finalized payroll run data must exist.
- **US-PAY-006**: Statutory deduction data needed for statutory reports.
- **US-CORE-xxx**: Department and employee master data for breakdowns.
- ClosedXML for Excel export (open-source).
- QuestPDF for PDF report generation (open-source).
- Hangfire for async report generation.

## 10. Assumptions & Constraints
- Custom report builder (drag-and-drop) is out of scope for Phase 1 (deferred to Phase 2, per technical doc section 33.5).
- Bank advice format varies by country and bank; Phase 1 supports a generic CSV/Excel format with tenant-configurable columns.
- Year-end tax statements follow the statutory format of the tenant's configured country.
- Chart data is pre-aggregated after each payroll finalization to ensure dashboard performance.

## 11. Test Hints
- Unit test: Verify payroll summary report totals match the sum of individual payslips for the period.
- Unit test: Verify bank advice file contains all employees from the finalized run with correct net amounts.
- Unit test: Verify year-end tax statement cumulative totals across 12 months match individual monthly payslips.
- Integration test: Generate a payroll report for Tenant A, verify no data from Tenant B is included.
- Integration test: Verify async report generation via Hangfire produces correct output file.
- Integration test: Verify Excel export opens correctly in spreadsheet applications (ClosedXML output validation).
- E2E (Playwright): Navigate to payroll reports, select summary report, apply department filter, verify filtered data, export to Excel, verify download.
- Performance test: Generate payroll register for 5,000 employees and verify completion within 2 minutes.
