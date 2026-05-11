---
id: US-PAY-005
module: Payroll
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-005: Employee Views and Downloads Payslips

## 1. Description
**As an** Employee,
**I want to** view and download my payslips for any month I have worked,
**So that** I can review my compensation details and maintain personal financial records.

## 2. Preconditions
- Employee is authenticated and has an active tenant membership.
- Employee has `Payroll.Read.Self` permission.
- At least one finalized payroll run exists that includes the employee.
- Payslip PDFs have been generated (US-PAY-004).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee is logged in and navigates to "My Payslips" | The page loads | A list of all payslips from finalized payroll runs is displayed, sorted by pay period (most recent first), showing: Pay Period, Gross Earnings, Deductions, Net Salary |
| AC-2 | Employee selects a specific payslip | Employee clicks on a payslip row | The payslip detail view shows the full earnings and deductions breakdown inline (Notion-style expandable card); a "Download PDF" button is available |
| AC-3 | Employee clicks "Download PDF" | The download initiates | The employee's payslip PDF is downloaded; the file is the tenant-branded PDF generated in US-PAY-004 |
| AC-4 | Employee A is logged in | Employee A attempts to access Employee B's payslip via URL manipulation (e.g., changing the payslip ID in the API URL) | The system returns 403 Forbidden; the `Payroll.Read.Self` permission restricts access to only the authenticated employee's own payslips |
| AC-5 | Employee accesses payslips on a mobile device | The payslip list and detail views render | The UI is fully responsive, readable on 360px screens, with the breakdown table scrollable horizontally if needed |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a "My Payslips" page accessible from the employee self-service portal navigation.
- FR-2: The system SHALL list all payslips for the authenticated employee from Finalized payroll runs only (not ReviewPending or Approved).
- FR-3: The system SHALL display payslip details inline with expandable sections for Earnings and Deductions, each showing component name and amount.
- FR-4: The system SHALL provide a PDF download endpoint: `GET /api/v1/payroll/my-payslips/{payslipId}/pdf` that returns the pre-generated PDF.
- FR-5: The system SHALL enforce `Self` scope on the `Payroll.Read` permission — employees can only access their own payslip data.
- FR-6: The system SHALL support filtering payslips by year and searching by pay period.
- FR-7: The system SHALL display YTD (year-to-date) totals on the payslip detail view if enabled by the tenant.
- FR-8: All payslip queries SHALL be scoped by `tenant_id` via RLS and by `employee_id` via application-level authorization.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Payslip list page SHALL load within 1.5 seconds (P95) as per the 2.5-second page-load target.
- NFR-2: Payslip PDF download SHALL initiate within 2 seconds.
- NFR-3: The payslip list and detail views SHALL be WCAG 2.1 AA accessible (per technical doc section 32).
- NFR-4: Employee payslip data SHALL never be leaked to other employees or other tenants (zero-tolerance cross-tenant/cross-employee data leak).
- NFR-5: Test coverage >= 85% for payslip access authorization logic.

## 6. Business Rules
- BR-1: Only payslips from Finalized payroll runs are visible to employees. Payslips from runs in ReviewPending or Approved status are hidden from the employee view.
- BR-2: Employees can view payslips for their entire employment history within the tenant, including months before which they were transferred between departments.
- BR-3: If an employee is terminated, their payslip access is governed by the tenant's post-termination access policy (configurable: immediate revoke, 30 days, or permanent read-only).
- BR-4: Payslip data is read-only for employees; no editing or deletion capability.
- BR-5: The system must not expose other employees' salary data in any way (list views, URLs, error messages, etc.).

## 7. Data Requirements

**API Response - Payslip List:**
```json
{
  "items": [
    {
      "payslipId": "uuid",
      "payMonth": 5,
      "payYear": 2026,
      "grossEarnings": 50000.00,
      "totalDeductions": 8500.00,
      "netSalary": 41500.00,
      "paidDays": 22,
      "lopDays": 0,
      "pdfAvailable": true
    }
  ],
  "totalCount": 24,
  "page": 1,
  "pageSize": 12
}
```

**API Response - Payslip Detail:**
```json
{
  "payslipId": "uuid",
  "payMonth": 5,
  "payYear": 2026,
  "employee": { "name": "...", "employeeNo": "...", "department": "...", "designation": "..." },
  "earnings": [
    { "componentName": "Basic Salary", "amount": 25000.00, "ytdAmount": 125000.00 }
  ],
  "deductions": [
    { "componentName": "EPF (Employee)", "amount": 3000.00, "ytdAmount": 15000.00 }
  ],
  "grossEarnings": 50000.00,
  "totalDeductions": 8500.00,
  "netSalary": 41500.00,
  "workingDays": 22,
  "paidDays": 22,
  "lopDays": 0
}
```

## 8. UI/UX Notes (Notion-like)
- "My Payslips" should be a prominent item in the employee self-service sidebar navigation with a receipt/document icon.
- Payslip list rendered as a Notion-style table with clean rows, subtle hover effects, and clear typography for salary amounts (right-aligned, monospace for numbers).
- Payslip detail opens as an expandable inline card (not a new page) with smooth animation; alternatively, a slide-over panel on desktop.
- Earnings shown in green-tinted rows; Deductions in red-tinted rows for visual clarity.
- PDF download button styled as a secondary action button with a download icon.
- Year filter rendered as horizontal tabs or a dropdown at the top of the list.
- Mobile: full-width card layout with stacked rows; PDF download button prominently placed; horizontal scroll for component breakdown table.
- Loading skeleton shown while payslip data fetches.

## 9. Dependencies
- **US-PAY-003**: Payroll run must be finalized for payslips to be visible.
- **US-PAY-004**: Payslip PDFs must be generated for download capability.
- **US-AUTH-xxx**: Employee authentication and `Payroll.Read.Self` permission enforcement.
- **US-CORE-xxx**: Employee profile data (name, department, designation) for display.

## 10. Assumptions & Constraints
- Employees cannot see payslips from other tenants they may belong to (cross-tenant user with multiple memberships sees only the active tenant's payslips).
- PDF downloads serve pre-generated files; on-demand PDF generation is not supported in the employee view.
- The payslip list is paginated (12 per page by default, representing one year of monthly payslips).
- Browser PDF viewer is used for inline preview; no custom PDF viewer component needed.

## 11. Test Hints
- Unit test: Verify that only Finalized payslips are returned in the employee's payslip list query.
- Integration test: Authenticate as Employee A, request Employee B's payslip by ID, verify 403 response.
- Integration test: Authenticate as Employee in Tenant A, verify no payslips from Tenant B are returned.
- Integration test: Verify payslip detail API returns correct component breakdown matching the payroll_slip_detail records.
- E2E (Playwright): Log in as employee, navigate to My Payslips, verify list shows correct payslips, click a payslip, verify detail card expands with correct data, download PDF, verify file downloads.
- Accessibility test: Run axe-core on the payslip list and detail views to verify WCAG 2.1 AA compliance.
- Mobile test (Playwright): Verify payslip views render correctly at 360px viewport width.
