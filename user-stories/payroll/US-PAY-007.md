---
id: US-PAY-007
module: Payroll
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-007: Payroll Adjustments (Bonus, Deductions, Reimbursements)

## 1. Description
**As an** HR Officer,
**I want to** create payroll adjustments (ad-hoc bonuses, one-time deductions, reimbursements, and corrections) for specific employees,
**So that** non-recurring compensation changes are accurately reflected in the next or a specific payroll run.

## 2. Preconditions
- Employee records exist with assigned salary structures (US-PAY-002).
- HR Officer has `Payroll.*.All` permission.
- Payroll module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR navigates to the Payroll Adjustments page | HR creates a new adjustment for Employee X: type=Bonus, amount=10,000, applicable period=June 2026 | The adjustment is saved and linked to the employee; it will be included in the June 2026 payroll run |
| AC-2 | An adjustment of type "Deduction" exists for an employee | The payroll run for the applicable period executes | The deduction amount is subtracted from the employee's net salary in the payslip |
| AC-3 | HR creates a reimbursement adjustment with supporting document | The adjustment is saved | The document is stored in blob storage at `{tenantId}/payroll/adjustments/{adjustmentId}/` and linked to the adjustment record |
| AC-4 | A payroll run for June 2026 is finalized | HR needs to correct an error in Employee Y's payslip | HR creates a correction adjustment for June 2026 that will be applied in the next payroll run (July 2026) as an arrears line item |
| AC-5 | Adjustments exist for Tenant A | A user from Tenant B queries the adjustments API | Only Tenant B's adjustments are returned; RLS enforces tenant isolation |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL allow creating payroll adjustments with fields: employee_id, adjustment_type (Bonus, Deduction, Reimbursement, Correction/Arrears), amount, applicable_pay_month, applicable_pay_year, description/reason, supporting_document (optional), is_taxable flag, is_recurring flag, recurrence_months (if recurring).
- FR-2: The system SHALL support bulk adjustments: upload a CSV with employee IDs, adjustment types, and amounts for a given period.
- FR-3: The payroll run engine SHALL automatically pick up all pending adjustments for the run period and include them as line items in the payslip.
- FR-4: The system SHALL mark adjustments as "Applied" after they are included in a finalized payroll run, preventing double application.
- FR-5: The system SHALL support recurring adjustments (e.g., monthly loan deduction for 12 months) with automatic inclusion in each payroll run until the recurrence period ends.
- FR-6: The system SHALL allow cancellation of pending (not yet applied) adjustments.
- FR-7: Correction/Arrears adjustments SHALL include a reference to the original payroll run and payslip being corrected.
- FR-8: All adjustment records SHALL carry `tenant_id` and be governed by PostgreSQL RLS policies.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Adjustment processing during payroll run SHALL add no more than 10% overhead to the total payroll run time.
- NFR-2: Bulk adjustment upload of 1,000 records SHALL complete within 30 seconds.
- NFR-3: All adjustment creation and modification SHALL be audit-logged with before/after values.
- NFR-4: Test coverage for adjustment processing logic SHALL be >= 85%.
- NFR-5: Supporting documents SHALL be validated for file type (PDF, JPG, PNG only) and size (max 5MB per file).

## 6. Business Rules
- BR-1: Adjustments can only be created for employees with an active salary structure assignment.
- BR-2: A bonus adjustment is treated as an earning; it increases gross salary and is subject to tax if `is_taxable = true`.
- BR-3: A deduction adjustment is subtracted from net salary; total deductions (including adjustments) cannot result in a negative net salary. If they would, the system must warn HR.
- BR-4: Reimbursements are non-taxable by default unless explicitly marked as taxable.
- BR-5: Correction/Arrears adjustments must reference the original payslip ID and clearly show as "Arrears" on the payslip.
- BR-6: Recurring adjustments auto-create pending adjustment records for each future period; HR can cancel remaining occurrences at any time.
- BR-7: An adjustment cannot be applied to a period that already has a Finalized payroll run; it must target the next available period.
- BR-8: Adjustments created after a payroll run enters Processing status for the target period are deferred to the next period.

## 7. Data Requirements

**payroll_adjustment table:**
| Column | Type | Constraints |
|--------|------|-------------|
| payroll_adjustment_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| employee_id | uuid (FK) | NOT NULL |
| adjustment_type | varchar(20) | NOT NULL (Bonus, Deduction, Reimbursement, Correction) |
| amount | numeric(18,2) | NOT NULL |
| description | text | NOT NULL |
| applicable_pay_month | int | NOT NULL |
| applicable_pay_year | int | NOT NULL |
| is_taxable | boolean | default false |
| is_recurring | boolean | default false |
| recurrence_end_month | int | nullable |
| recurrence_end_year | int | nullable |
| status | varchar(20) | NOT NULL (Pending, Applied, Cancelled) |
| applied_in_payroll_run_id | uuid (FK) | nullable |
| reference_payroll_slip_id | uuid (FK) | nullable (for corrections) |
| supporting_document_path | varchar(500) | nullable |
| created_by | uuid (FK) | NOT NULL |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

## 8. UI/UX Notes (Notion-like)
- Payroll Adjustments page: Notion-style database table view with filters (Status, Type, Period, Employee), sortable columns, and inline status badges.
- "New Adjustment" opens a slide-over panel with a clean form: employee search/select (typeahead), type dropdown, amount, period selector, description, file upload for supporting documents.
- Bulk upload section: drag-and-drop CSV file area with a template download link and validation preview before committing.
- Recurring adjustment configuration shows a preview of all future periods that will be affected.
- Applied adjustments are dimmed or moved to an "Applied" tab to reduce clutter.
- Mobile: individual adjustment creation supported; bulk upload is desktop-only.

## 9. Dependencies
- **US-PAY-002**: Employee salary structure must be assigned.
- **US-PAY-003**: Payroll run engine must process adjustments during payroll calculation.
- **US-PAY-004**: Adjustment line items must appear on generated payslips.
- Blob storage for supporting documents.

## 10. Assumptions & Constraints
- Adjustments are a simple flat-amount mechanism; complex adjustment formulas (e.g., percentage of salary) are out of scope for Phase 1.
- Supporting documents are stored in blob storage alongside other payroll documents.
- CSV bulk upload format: employee_no, adjustment_type, amount, description, is_taxable (header row required).
- Recurring adjustments generate future pending records up-front; if the employee separates, remaining pending adjustments are automatically cancelled.

## 11. Test Hints
- Unit test: Verify bonus adjustment increases gross salary and triggers tax recalculation when is_taxable=true.
- Unit test: Verify deduction adjustment that would cause negative net salary triggers a validation warning.
- Unit test: Verify recurring adjustment creates correct number of future pending records.
- Integration test: Create adjustment for period, run payroll, verify adjustment appears in payslip detail, verify status changes to Applied.
- Integration test: Attempt to apply adjustment to a Finalized period and verify it targets the next period.
- Integration test: Verify tenant isolation on adjustments API.
- E2E (Playwright): Create a bonus adjustment, run payroll, verify the bonus appears on the employee's payslip.
- Bulk test: Upload CSV with 500 adjustments and verify all are created correctly.
