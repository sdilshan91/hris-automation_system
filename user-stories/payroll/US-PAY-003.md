---
id: US-PAY-003
module: Payroll
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 7
---

# US-PAY-003: Run Monthly Payroll for All Employees

## 1. Description
**As an** HR Officer,
**I want to** initiate and execute a monthly payroll run for all active employees in my tenant,
**So that** salaries are calculated accurately, incorporating attendance, leave, deductions, and earnings for the period.

## 2. Preconditions
- Salary structures are assigned to all active employees (US-PAY-002).
- Attendance and leave data for the payroll period is finalized.
- Statutory deduction rules are configured (US-PAY-006).
- HR Officer has `Payroll.Run` permission.
- No other payroll run for the same period is in progress or finalized for this tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR selects a payroll period (e.g., May 2026) and initiates a run | The system receives the request | A payroll_run record is created with status=Queued, a Hangfire job is enqueued, and the API returns 202 Accepted with the runId |
| AC-2 | A payroll run is in progress | The Hangfire worker processes the run | It locks attendance and leave for the period, fetches all active employees with salary structures, computes earnings, deductions, LOP, statutory amounts, and net salary for each employee |
| AC-3 | Payroll calculation is complete | The run finishes | Individual payslip records are persisted, the run status changes to ReviewPending, and HR receives an in-app notification (SignalR) and email |
| AC-4 | A payroll run for May 2026 already exists with status=Finalized | HR attempts to create another run for May 2026 | The system rejects the request with a 409 Conflict error indicating the period is already finalized |
| AC-5 | 5,000 employees exist in the tenant | HR runs payroll | The entire payroll run completes within 10 minutes (NFR from technical document section 1.4) |
| AC-6 | An employee has no salary structure assigned | The payroll run executes | That employee is skipped with a warning entry in the run log; the run continues for remaining employees |
| AC-7 | HR is from Tenant A | HR initiates payroll | Only employees belonging to Tenant A are included; RLS enforces tenant isolation throughout the entire computation pipeline |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL create a `payroll_run` record with fields: tenant_id, pay_month, pay_year, status (Queued, Processing, ReviewPending, Approved, Finalized, Cancelled), initiated_by, initiated_at.
- FR-2: The system SHALL enqueue a `ProcessPayrollRunJob` via Hangfire with tenant_id and run_id as parameters.
- FR-3: The Hangfire worker SHALL restore `ITenantContext` from job arguments to ensure RLS is applied throughout the job execution.
- FR-4: The system SHALL lock attendance and leave records for the payroll period to prevent modifications while the run is in progress.
- FR-5: For each active employee with a salary structure, the system SHALL:
  - a. Fetch the employee's salary components and their values.
  - b. Calculate LOP (Loss of Pay) based on attendance data (absent days without approved leave).
  - c. Apply statutory deductions (tax, social security) per configured rules.
  - d. Apply any payroll adjustments (bonuses, ad-hoc deductions, reimbursements) for the period.
  - e. Compute gross earnings, total deductions, and net salary.
  - f. Persist a `payroll_slip` record with a `payroll_slip_detail` record per component.
- FR-6: The system SHALL provide a real-time progress indicator (via SignalR) showing processed/total employee count.
- FR-7: The system SHALL support re-running payroll for a period if the current run is in ReviewPending or Cancelled status (previous slip data is replaced).
- FR-8: The system SHALL generate a payroll run summary with totals: total gross, total deductions, total net, total statutory, employee count, skipped count.
- FR-9: The payroll trigger endpoint SHALL accept an `Idempotency-Key` header to prevent duplicate runs (as per technical doc section 20).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Full payroll for 5,000 employees SHALL complete in < 10 minutes (per technical doc section 1.4, success criteria #3).
- NFR-2: The Hangfire job SHALL be idempotent and safely re-runnable (per technical doc section 28.5).
- NFR-3: The job SHALL use distributed locks to prevent concurrent payroll runs for the same tenant+period (per technical doc section 28.5).
- NFR-4: Each job execution SHALL log start/end times, correlation ID, processed-record counts, and tenant context (per technical doc section 28.5).
- NFR-5: Test coverage for payroll calculation logic SHALL be >= 85% with golden dataset unit tests (per technical doc section 39).
- NFR-6: The payroll run SHALL use batch inserts (bulk copy) for payslip records to optimize database write performance.
- NFR-7: Salary structure data SHALL be read from Redis cache during payroll processing to reduce database load.

## 6. Business Rules
- BR-1: Only ONE payroll run per tenant per pay period can be in a non-cancelled state at any time.
- BR-2: LOP is calculated as: `(daily_rate * LOP_days)` where `daily_rate = monthly_basic / working_days_in_month` and `LOP_days = absent_days_without_approved_leave`.
- BR-3: Payroll run cannot be initiated if attendance for the period is not yet locked/finalized.
- BR-4: Employees who joined mid-month receive pro-rated salary based on actual working days from their date of joining.
- BR-5: Employees who separated mid-month receive pro-rated salary based on actual working days until their last working day.
- BR-6: Payroll run status transitions: Queued -> Processing -> ReviewPending -> Approved -> Finalized. Also: any pre-Finalized status -> Cancelled.
- BR-7: Once a payroll run is Finalized, it is immutable. Any corrections must go through a payroll adjustment (US-PAY-007).
- BR-8: The system must handle rounding consistently (round half-up to 2 decimal places) and ensure the sum of component amounts equals the declared net salary (penny reconciliation).
- BR-9: Overtime earnings from attendance module are included as an earning component if configured in the salary structure.

## 7. Data Requirements

**payroll_run table:**
| Column | Type | Constraints |
|--------|------|-------------|
| payroll_run_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| pay_month | int | NOT NULL (1-12) |
| pay_year | int | NOT NULL |
| status | varchar(20) | NOT NULL (Queued, Processing, ReviewPending, Approved, Finalized, Cancelled) |
| total_employees | int | default 0 |
| processed_employees | int | default 0 |
| skipped_employees | int | default 0 |
| total_gross | numeric(18,2) | default 0 |
| total_deductions | numeric(18,2) | default 0 |
| total_net | numeric(18,2) | default 0 |
| initiated_by | uuid (FK) | NOT NULL |
| initiated_at | timestamptz | NOT NULL |
| completed_at | timestamptz | nullable |
| approved_by | uuid (FK) | nullable |
| approved_at | timestamptz | nullable |
| finalized_at | timestamptz | nullable |
| idempotency_key | varchar(100) | UNIQUE per tenant |
| created_at | timestamptz | NOT NULL |

**payroll_slip table:**
| Column | Type | Constraints |
|--------|------|-------------|
| payroll_slip_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| payroll_run_id | uuid (FK) | NOT NULL |
| employee_id | uuid (FK) | NOT NULL |
| gross_earnings | numeric(18,2) | NOT NULL |
| total_deductions | numeric(18,2) | NOT NULL |
| net_salary | numeric(18,2) | NOT NULL |
| lop_days | numeric(5,2) | default 0 |
| working_days | numeric(5,2) | NOT NULL |
| paid_days | numeric(5,2) | NOT NULL |
| pay_month | int | NOT NULL |
| pay_year | int | NOT NULL |
| created_at | timestamptz | NOT NULL |

**payroll_slip_detail table:**
| Column | Type | Constraints |
|--------|------|-------------|
| payroll_slip_detail_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| payroll_slip_id | uuid (FK) | NOT NULL |
| salary_component_id | uuid (FK) | NOT NULL |
| component_name | varchar(100) | NOT NULL (denormalized for history) |
| component_type | varchar(20) | NOT NULL (Earning, Deduction, Statutory, Reimbursement) |
| amount | numeric(18,2) | NOT NULL |
| calculation_basis | text | nullable (e.g., "40% of 50000") |

**Composite index:** `payroll_slip(tenant_id, payroll_run_id, employee_id)` per technical doc section 19.12.

## 8. UI/UX Notes (Notion-like)
- Payroll Runs page: a Notion-style table view listing all payroll runs with columns: Period, Status (color-coded badge), Employees, Total Net, Initiated By, Date. Sortable and filterable.
- "New Payroll Run" button opens a modal with: Pay Month (dropdown), Pay Year (dropdown), and a pre-run validation summary (e.g., "247 employees ready, 3 missing salary structure").
- During processing: show a progress bar with real-time updates via SignalR (e.g., "Processing 1,247 / 5,000 employees...").
- After completion: run summary card with key totals and a "View Details" link to the payslip list.
- Status workflow shown as a horizontal stepper: Queued > Processing > Review > Approved > Finalized.
- Mobile: payroll run initiation available; progress viewable; detailed payslip list deferred to desktop.

## 9. Dependencies
- **US-PAY-001**: Salary structures and components configured.
- **US-PAY-002**: Salary structures assigned to employees.
- **US-PAY-006**: Statutory deduction rules configured.
- **US-PAY-010**: Attendance and leave data integration for LOP calculation.
- **US-ATT-xxx**: Attendance module must provide finalized attendance data for the period.
- **US-LEAVE-xxx**: Leave module must provide approved leave data for the period.
- Hangfire infrastructure must be operational (PostgreSQL-backed, per dev-instructions).

## 10. Assumptions & Constraints
- Payroll run is a monthly cycle; weekly/bi-weekly pay schedules are out of scope for Phase 1.
- The Hangfire worker runs in-process (single server deployment in Phase 1); horizontal scaling of workers is a Phase 2 concern.
- All monetary calculations use `numeric(18,2)` to avoid floating-point issues.
- The attendance lock mechanism is advisory (prevents UI edits) not a database-level lock to avoid long-running transactions.
- Time zone for payroll period boundaries is determined by the tenant's configured time zone.

## 11. Test Hints
- Unit test: Golden dataset tests for payroll calculation with known inputs (salary, attendance, deductions) and expected outputs.
- Unit test: Verify LOP calculation for employees with 3 absent days in a 22-working-day month.
- Unit test: Verify pro-rated salary for mid-month joiner (joined on 15th of a 30-day month).
- Unit test: Verify penny reconciliation (sum of components = net salary).
- Integration test: Initiate payroll run, verify Hangfire job is enqueued, verify status transitions.
- Integration test: Attempt duplicate run for same period and verify 409 Conflict.
- Integration test: Verify tenant isolation - Tenant A's payroll run does not include Tenant B's employees.
- Performance test: Run payroll for 5,000 employees and verify completion in < 10 minutes.
- E2E (Playwright): Initiate payroll run, wait for completion notification, verify summary totals.
