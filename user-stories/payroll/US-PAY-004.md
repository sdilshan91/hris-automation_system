---
id: US-PAY-004
module: Payroll
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-004: Generate Individual Payslips

## 1. Description
**As an** HR Officer,
**I want to** generate individual payslip documents (PDF) for each employee after a payroll run is finalized,
**So that** employees have a formal record of their compensation breakdown for each pay period.

## 2. Preconditions
- A payroll run for the target period has been completed with status ReviewPending, Approved, or Finalized (US-PAY-003).
- Payslip records exist in the `payroll_slip` and `payroll_slip_detail` tables.
- Tenant has a configured payslip template (or uses the system default template).
- HR Officer has `Payroll.*.All` permission.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A payroll run is in ReviewPending or Finalized status | HR clicks "Generate Payslips" | The system enqueues a Hangfire job to render PDF payslips for all employees in the run; each PDF is stored in blob storage at `{tenantId}/payroll/{runId}/{employeeId}.pdf` |
| AC-2 | Payslip PDFs are being generated | The generation process executes | Each PDF contains: employee name, employee number, department, designation, pay period, earnings breakdown, deductions breakdown, statutory items, net salary, company name, and company logo (per tenant branding) |
| AC-3 | Payslip PDFs are generated | HR views the payroll run detail page | A "Download All" button is available that generates a ZIP archive of all payslips; individual payslip PDFs are downloadable per employee |
| AC-4 | Payslips are generated for Tenant A | A user from Tenant B attempts to access the payslip storage path | Access is denied; blob storage paths are tenant-scoped and API enforces RLS |
| AC-5 | HR needs to regenerate payslips due to template change | HR clicks "Regenerate Payslips" on a non-finalized run | Existing PDFs are overwritten with newly rendered versions using the updated template |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL generate PDF payslips using QuestPDF (free, open-source as per technical doc section 33.4).
- FR-2: Each payslip PDF SHALL include: tenant company name, tenant logo, employee details (name, ID, department, designation), pay period, all earning components with amounts, all deduction components with amounts, statutory deductions itemized, gross earnings total, total deductions, net salary, working days, paid days, LOP days.
- FR-3: The system SHALL support per-tenant payslip templates allowing customization of: logo, company address, footer text, color scheme, and additional custom fields.
- FR-4: Payslip PDF generation SHALL be performed as a Hangfire background job to avoid blocking the API.
- FR-5: Generated PDFs SHALL be stored in blob storage (local filesystem or cloud) organized by tenant: `{tenantId}/payroll/{runId}/{employeeId}.pdf`.
- FR-6: The system SHALL provide an API endpoint to download a single payslip PDF and a bulk download endpoint returning a ZIP file.
- FR-7: The system SHALL record the generation timestamp and status per payslip (Generated, Failed) in the `payroll_slip` table.
- FR-8: Failed payslip generations SHALL be logged with error details and retryable individually.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Payslip PDF generation for 5,000 employees SHALL complete within 5 minutes (as part of the overall 10-minute payroll NFR).
- NFR-2: Each payslip PDF SHALL be <= 200KB in file size for efficient storage and email delivery.
- NFR-3: PDF generation SHALL be parallelized (batch processing with configurable concurrency, e.g., 10 concurrent renders).
- NFR-4: Test coverage for payslip rendering logic SHALL be >= 85%.
- NFR-5: Payslip PDFs SHALL NOT contain executable content (no JavaScript in PDFs).
- NFR-6: Blob storage paths SHALL be validated to prevent path traversal attacks.

## 6. Business Rules
- BR-1: Payslips can only be generated for payroll runs with status ReviewPending, Approved, or Finalized.
- BR-2: Payslip content is a point-in-time snapshot; even if salary components are later renamed, the payslip retains the original component names (denormalized in `payroll_slip_detail`).
- BR-3: The payslip must include a disclaimer/footer as configured by the tenant (e.g., "This is a computer-generated document and does not require a signature").
- BR-4: Year-to-date (YTD) totals for each component SHALL be included on the payslip if the tenant enables YTD display.
- BR-5: Payslip PDF file names SHALL follow the format: `{EmployeeNo}_{PayMonth}_{PayYear}.pdf` for easy identification.
- BR-6: Payslips for terminated employees who were paid in their final month must still be generated.

## 7. Data Requirements

**Additional columns on payroll_slip table (extending US-PAY-003):**
| Column | Type | Constraints |
|--------|------|-------------|
| pdf_generated_at | timestamptz | nullable |
| pdf_storage_path | varchar(500) | nullable |
| pdf_status | varchar(20) | nullable (Pending, Generated, Failed) |
| pdf_file_size_bytes | int | nullable |

**Payslip PDF Content Structure:**
- Header: Company logo, company name, address, payslip title, pay period
- Employee Section: Name, Employee No, Department, Designation, Date of Joining, Bank Account (masked)
- Earnings Table: Component Name | Monthly Amount | YTD Amount (if enabled)
- Deductions Table: Component Name | Monthly Amount | YTD Amount (if enabled)
- Summary: Gross Earnings, Total Deductions, Net Pay
- Footer: Days (Working, Paid, LOP), custom footer text

## 8. UI/UX Notes (Notion-like)
- Payroll run detail page should show a payslip generation status bar (e.g., "4,850 / 5,000 generated").
- Individual payslip preview should render inline in the browser (PDF viewer embedded in a modal) before downloading.
- "Download All" button should show estimated ZIP file size and initiate async generation with a download link sent via notification.
- Payslip list within a run should be a searchable, filterable Notion-style table: Employee Name | Employee No | Department | Net Salary | PDF Status | Actions (View/Download).
- Mobile: individual payslip view and download supported; bulk download not available on mobile.
- Tenant branding colors applied to the payslip PDF header and footer sections.

## 9. Dependencies
- **US-PAY-003**: Payroll run must be completed with payslip records persisted.
- **US-PAY-001**: Salary component names denormalized into payslip details.
- **US-TENANT-xxx**: Tenant branding (logo, company name, address) must be configured.
- Blob storage infrastructure must be available (local filesystem in dev, cloud storage in production).
- QuestPDF library (open-source) for PDF rendering.

## 10. Assumptions & Constraints
- QuestPDF is used for PDF generation (free, open-source, .NET-native; per technical doc section 33.4).
- Blob storage in Phase 1 uses local filesystem; migration to cloud storage (e.g., Azure Blob, S3) planned for Phase 2.
- Payslip templates are pre-built with configurable fields; a full drag-and-drop template designer is out of scope for Phase 1.
- PDF rendering is CPU-intensive; Hangfire worker concurrency must be tuned based on server resources.

## 11. Test Hints
- Unit test: Verify payslip PDF contains all required sections (header, employee info, earnings, deductions, summary, footer).
- Unit test: Verify YTD calculation sums across prior months' payslips for the same fiscal year.
- Integration test: Generate payslip for an employee, verify PDF is stored at the correct blob path.
- Integration test: Verify Tenant A cannot access Tenant B's payslip storage path via API.
- Integration test: Verify payslip regeneration overwrites existing PDF and updates timestamp.
- E2E (Playwright): Complete a payroll run, generate payslips, download one, verify PDF opens correctly.
- Performance test: Generate 5,000 payslip PDFs and verify completion within 5 minutes.
- Visual test: Compare generated payslip PDF against a golden reference PDF for layout consistency.
