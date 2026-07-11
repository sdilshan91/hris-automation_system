---
id: US-LV-011
module: Leave Management
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-LV-011: Compulsory Leave / Loss of Pay (LOP) Handling

## 1. Description
**As an** HR Officer,
**I want to** mark leave as Loss of Pay (LOP) when an employee has exhausted their leave balance or is absent without approved leave,
**So that** payroll accurately deducts salary for unpaid absence days.

## 2. Preconditions
- Employee records and leave balances are set up.
- An "Unpaid Leave" or "LOP" leave type exists in the tenant configuration.
- Payroll module is integrated and can consume LOP data.
- User has `Leave.Manage` or `HR.Officer` permission.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An employee applies for leave but has zero balance for the selected leave type (and negative balance is not allowed) | The employee submits the request | The system prompts: "Insufficient balance. This will be processed as Loss of Pay (LOP)." If the employee confirms, the request is created with `leave_type = LOP` and `is_lop = true` |
| AC-2 | An employee is marked absent (no clock-in, no approved leave) for a working day | HR runs the monthly attendance reconciliation | The system auto-generates an LOP leave entry for the absent day, creating a `leave_request` with type "LOP" and status "System-Generated" |
| AC-3 | HR Officer manually assigns LOP days to an employee | They use the "Assign LOP" action on the employee's leave record | An LOP ledger entry is created with the specified days, a `leave_request` record is created with status "HR-Assigned", and the employee is notified |
| AC-4 | Payroll run is initiated for a month | The system calculates LOP deductions | The payroll engine queries LOP leave entries for the period and calculates salary deduction as: `(monthly_salary / working_days) * lop_days`; the deduction appears as a line item on the payslip |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: LOP leave type is a special system leave type that is auto-created during tenant setup; it cannot be deleted but can be renamed.
- FR-2: Auto-LOP generation: Hangfire job (`ProcessAbsenteeismJob`) runs daily (or on-demand) to detect unaccounted absences and generate LOP entries.
- FR-3: Manual LOP assignment: API endpoint `POST /api/v1/leaves/assign-lop` with body `{ employeeId, dates[], reason }`.
- FR-4: LOP entries are stored in `leave_request` with a flag `is_lop = true` and special statuses: "System-Generated", "HR-Assigned".
- FR-5: LOP data exposed to payroll via: `GET /api/v1/leaves/lop-summary?employeeId={id}&from={date}&to={date}`.
- FR-6: Compulsory leave (e.g., company shutdown days): HR can bulk-assign a specific leave type for all employees for specific dates.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Auto-LOP job for 5,000 employees must complete within 3 minutes.
- NFR-2: All LOP data tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-3: LOP entries must be immutable once payroll is finalized for the period (no edits/deletes allowed).
- NFR-4: Audit trail for all LOP assignments (auto and manual).

## 6. Business Rules
- BR-1: LOP has no entitlement/balance — it is purely a deduction mechanism.
- BR-2: LOP salary deduction formula is tenant-configurable: `(basic_salary / total_working_days) * lop_days` or `(gross_salary / calendar_days) * lop_days`.
- BR-3: System-generated LOP entries can be overridden by HR (e.g., if an employee later provides a valid reason, HR can convert LOP to a different leave type or remove it).
- BR-4: Compulsory leave (company shutdown) deducts from the employee's relevant leave balance first; if insufficient, it becomes LOP.
- BR-5: LOP entries for a payroll-locked period cannot be modified.
- BR-6: Employees must be notified whenever LOP is assigned (auto or manual).

## 7. Data Requirements
- **Table:** `leave_request` — extended with `is_lop (boolean)`, `lop_source (varchar(20))` [employee_request, system_generated, hr_assigned, compulsory].
- **Payroll integration:** `leave_request` records with `is_lop = true` are queried during payroll computation.
- **Table:** `compulsory_leave` (optional) — for bulk company shutdown: `compulsory_leave_id`, `tenant_id`, `date`, `leave_type_id`, `reason`, audit columns.

## 8. UI/UX Notes (Notion-like)
- LOP section on the HR Leave Management page with filters: auto-generated, HR-assigned, employee-requested.
- Bulk LOP assignment via a multi-select employee picker + date range + reason form.
- Compulsory leave assignment via a date picker + leave type + "Apply to all" action.
- LOP entries highlighted in red/orange in the employee's leave history.
- Override action: HR can click on a system-generated LOP entry and convert it to a different leave type (dropdown + reason).
- Mobile: LOP management accessible but optimized for desktop (complex actions).

## 9. Dependencies
- **US-LV-001**: LOP leave type must exist as a system leave type.
- **US-LV-002**: Entitlements determine when LOP is triggered (balance exhaustion).
- **US-ATTENDANCE-***: Attendance data is required for auto-LOP generation (absenteeism detection).
- **US-PAYROLL-***: Payroll module consumes LOP data for salary deductions.
- **Hangfire**: For the daily absenteeism detection job.
- **Notification Service**: For LOP assignment notifications.

## 10. Assumptions & Constraints
- Auto-LOP generation depends on attendance data being up-to-date; if attendance is not captured for a day, it is considered absent.
- Compulsory leave (shutdown) is a Phase 1 feature but bulk assignment UI may be simplified (date + select all).
- The LOP salary formula is configurable per tenant but defaults to basic-salary-based calculation.
- Integration between leave and payroll is via shared database queries (not API calls) for performance.

## 11. Test Hints
- Test auto-LOP: Employee has no clock-in and no approved leave for Monday; run absenteeism job; verify LOP entry created.
- Test manual LOP: HR assigns 2 LOP days; verify leave request and ledger entries created, employee notified.
- Test payroll integration: Employee has 3 LOP days in the period; verify payroll deduction amount = `(salary/working_days) * 3`.
- Test override: Convert a system-generated LOP entry to Casual Leave; verify balance deduction and LOP removal.
- Test compulsory leave: Assign company shutdown for all employees; verify leave deducted from balance or LOP created for those with insufficient balance.
- Test payroll lock: Attempt to modify LOP for a locked period; verify rejection.
- Test tenant isolation: LOP data in Tenant A must not affect Tenant B.
