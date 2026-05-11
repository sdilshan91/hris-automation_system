---
id: US-RPT-003
module: Reports & Analytics
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-RPT-003: Payroll Reports and Summaries

## 1. Description
**As an** HR Officer (or Payroll Administrator),
**I want to** generate payroll reports including payroll run summaries, department-wise salary distribution, statutory deduction reports, and cost-to-company analysis,
**So that** I can verify payroll accuracy, provide finance with salary disbursement summaries, and ensure compliance with statutory requirements.

## 2. Preconditions
- The HR Officer is authenticated and has `Reports.View.All` or `Payroll.View.All` permission within their tenant.
- At least one payroll run has been completed (finalized) in the tenant.
- The Payroll and Reporting modules are enabled for the tenant's subscription plan.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer selects "Payroll Run Summary" for March 2026 | They generate the report | The report displays: total gross pay, total deductions (broken down by statutory and voluntary), total net pay, employee count processed, and a comparison bar chart vs. the previous month. All amounts are in the tenant's configured currency. |
| AC-2 | HR Officer selects "Department-wise Salary Distribution" | They generate the report | A stacked bar chart shows total salary cost per department, broken down by earnings components (basic, HRA, allowances). A data table below shows per-department totals and employee counts. |
| AC-3 | HR Officer selects "Statutory Deductions Report" for the current fiscal year | They generate the report | A table lists all statutory deduction types (tax, social security, pension fund, etc.) with monthly totals and year-to-date cumulative amounts. Downloadable for submission to statutory authorities. |
| AC-4 | HR Officer selects "Bank Advice Report" for the latest payroll run | They generate the report | A table displays employee name, bank name, account number, IFSC/SWIFT code, and net pay amount. The report can be exported in the tenant's configured bank advice format (CSV/text). |
| AC-5 | Payroll reports are generated in Tenant A | A user from Tenant B accesses the payroll reports | Only Tenant B's payroll data is shown; RLS and EF Core filters enforce complete tenant isolation on all salary and payroll data. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide the following payroll reports: Payroll Run Summary, Department-wise Salary Distribution, Statutory Deductions, Bank Advice, Cost-to-Company Analysis, Earnings & Deductions Breakdown, Year-End Tax Statement Summary.
- FR-2: Each report SHALL support filters: payroll period (month/year), department, location, pay grade, employment type.
- FR-3: Payroll Run Summary SHALL show month-over-month comparison with variance highlighting (increase in red, decrease in green).
- FR-4: The system SHALL support multiple payroll runs per period (e.g., supplementary runs) and allow selection of specific runs.
- FR-5: Reports SHALL render charts using Chart.js / ngx-charts and provide a data table toggle.
- FR-6: The system SHALL mask sensitive data (bank account numbers) by default, showing only last 4 digits, with a "Reveal" toggle requiring `Payroll.ViewSensitive` permission.
- FR-7: Report data SHALL be cached in Redis (TTL 15 minutes) for repeat access.
- FR-8: The system SHALL scope all payroll data by `tenant_id` from the session context.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Payroll report generation SHALL complete within 5 seconds (P95) for tenants with up to 5,000 employees.
- NFR-2: All payroll data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Bank account numbers and salary figures are classified as PII; access to these fields SHALL be audited (action "PayrollReport.ViewSensitive").
- NFR-4: The report UI SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The report UI SHALL meet WCAG 2.1 AA accessibility standards.
- NFR-6: Heavy payroll reports SHALL use read replicas if configured.

## 6. Business Rules
- BR-1: Only finalized payroll runs are included in reports; draft or in-progress runs are excluded.
- BR-2: Salary figures are displayed in the tenant's configured currency with the appropriate currency symbol and decimal formatting.
- BR-3: Statutory deduction categories are configured per tenant based on their country/jurisdiction.
- BR-4: Bank advice reports follow the tenant's configured bank advice format (customizable by Tenant Admin).
- BR-5: Year-end tax statements aggregate all payroll runs within the tenant's fiscal year.
- BR-6: Cost-to-Company includes employer contributions (pension, insurance) in addition to gross pay.

## 7. Data Requirements
**Filter inputs:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| report_type | varchar(50) | Yes | Valid payroll report key |
| payroll_period | varchar(7) | Yes | Format: YYYY-MM |
| payroll_run_id | uuid | No | Specific run (defaults to latest) |
| department_ids | uuid[] | No | Must exist in tenant |
| location_ids | uuid[] | No | Must exist in tenant |
| pay_grade | varchar(50)[] | No | Valid grade values |

**Output:** Report object with summary metrics, chart data, and tabular data with appropriate PII masking.

## 8. UI/UX Notes
- Payroll report section: tabbed navigation (Run Summary | Salary Distribution | Statutory | Bank Advice | CTC Analysis).
- Summary cards at the top: Total Gross, Total Deductions, Total Net Pay, Employee Count -- each as a KPI card with a comparison arrow (up/down vs. previous period).
- Month-over-month comparison: dual bar chart with current month and previous month side by side.
- Bank advice table: masked account numbers with an eye icon toggle for revealing (with permission check).
- Statutory report: exportable format matching regulatory submission requirements.
- On mobile (< 768px): KPI cards scroll horizontally; charts stack vertically; tables horizontally scrollable.
- Print-friendly layout for statutory reports.

## 9. Dependencies
- Payroll module: Payroll run data, salary structures, deductions.
- US-CHR-001: Employee records for employee context.
- US-CHR-004: Department data for grouping.
- US-RPT-004: Export functionality.
- US-NTF-004: Audit trail for sensitive data access.
- Authentication module: Permission-based access to sensitive payroll data.

## 10. Assumptions & Constraints
- Payroll run data is generated by the Payroll module; this story only reads and displays the data.
- Statutory deduction types are configured during tenant setup based on the tenant's country.
- Bank advice format is configurable per tenant.
- Chart.js or ngx-charts (free/open-source) is used for visualizations.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Complete a payroll run for 50 employees; generate Payroll Run Summary; verify totals match the sum of individual payslips.
- **Month-over-month:** Run payroll for two consecutive months; verify comparison chart shows correct variances.
- **Department distribution:** Assign employees to 3 departments; verify stacked bar chart shows correct salary breakdown per department.
- **Statutory deductions:** Configure 3 statutory deduction types; verify the report totals match payslip-level deductions.
- **Bank advice:** Generate bank advice; verify account numbers are masked; reveal with permission; verify full numbers shown.
- **Tenant isolation:** Generate payroll reports in Tenant A and B; verify no cross-tenant data leakage.
- **PII audit:** Access bank advice with full account numbers; verify audit record created with "PayrollReport.ViewSensitive".
- **Draft exclusion:** Create a payroll run but do not finalize; verify it does not appear in reports.
- **Responsive:** Test KPI cards and charts at 360px width.
