---
id: US-ATT-009
module: Attendance
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-009: Attendance Integration with Payroll (Feeding Hours/Days)

## 1. Description
**As an** HR Officer,
**I want** the attendance module to automatically feed working days, overtime hours, and loss-of-pay days into the payroll module,
**So that** salary calculations are accurate and the payroll process does not require manual attendance data entry.

## 2. Preconditions
- Both the Attendance and Payroll modules must be enabled for the tenant.
- The monthly attendance summary must be generated (US-ATT-007) for the payroll period.
- The payroll period must not yet be finalized.
- Salary components linked to attendance (LOP deduction, overtime allowance) must be configured.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Monthly attendance summary is generated and payroll run is initiated | HR Officer starts the payroll run for the month | The system automatically pulls attendance data (present days, LOP days, approved overtime hours) and populates the payroll calculation inputs |
| AC-2 | An employee has 2 LOP days in the month | The payroll engine processes the employee's salary | The system deducts (monthly_salary / total_working_days) * 2 from the gross pay as LOP deduction |
| AC-3 | An employee has 10 hours of approved overtime at 1.5x rate | The payroll engine processes the employee's salary | The system adds overtime_pay = (hourly_rate * 1.5 * 10) to the gross pay |
| AC-4 | HR Officer locks attendance for the payroll period | HR clicks "Lock Attendance" for the period | All attendance records for the period are locked (no further clock-in/out, regularization, or modifications allowed), and payroll can proceed |
| AC-5 | Attendance data changes after payroll run has started but before finalization | HR Officer unlocks the attendance period to correct an error | The system allows the correction, recalculates affected payroll entries, and re-locks the period when HR confirms |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide an API endpoint that the payroll module calls to fetch attendance summary data for a given tenant, period, and employee list.
- FR-2: The API shall return per employee: `total_present_days`, `total_absent_days`, `lop_days`, `approved_overtime_minutes`, `total_work_minutes`, `late_deduction_days`.
- FR-3: The system shall provide an "Attendance Lock" feature that freezes all attendance data for a specified date range, preventing any modifications.
- FR-4: The system shall log the lock/unlock actions in the audit log with the HR Officer's ID and timestamp.
- FR-5: The system shall support a reconciliation view showing attendance data side-by-side with payroll inputs for each employee, highlighting any discrepancies.
- FR-6: The system shall trigger attendance data refresh in payroll when attendance records are modified (via regularization approval) during an active payroll run.
- FR-7: The system shall calculate LOP as: `lop_days = absent_days - (approved_leave_days_that_cover_absences)`. Only unexcused absences result in LOP.
- FR-8: The system shall calculate overtime pay inputs using approved overtime records only (pending/rejected overtime is excluded).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The attendance-to-payroll API must return data for up to 5,000 employees within 5 seconds.
- NFR-2: The attendance lock operation must be atomic and enforce a database-level constraint (e.g., a `locked_periods` table with range checks).
- NFR-3: PostgreSQL RLS must enforce tenant isolation on all attendance data accessed by the payroll module.
- NFR-4: Data consistency between attendance and payroll must be guaranteed; no partial reads during payroll computation.
- NFR-5: The reconciliation view must load within 3 seconds at P95.

## 6. Business Rules
- BR-1: Attendance data must be locked before a payroll run can be finalized. The system shall enforce this as a precondition.
- BR-2: LOP deduction formula: `lop_deduction = (basic_salary / total_working_days_in_month) * lop_days`.
- BR-3: Overtime pay formula: `overtime_pay = (basic_salary / (total_working_days * shift_hours) ) * overtime_hours * multiplier`.
- BR-4: Late arrival deductions (from BR-4 in US-ATT-008) are converted to LOP days and included in the LOP total.
- BR-5: Only approved regularizations and approved overtime are included in payroll data. Pending items are excluded.
- BR-6: If an attendance period is unlocked after payroll has started, all affected payroll slips must be recalculated.
- BR-7: Attendance data for terminated employees is included up to their last working day.
- BR-8: The payroll cutoff date (e.g., 25th of the month) determines which attendance days are included in the current payroll period.

## 7. Data Requirements
**Attendance-to-payroll API response (per employee):**
| Field | Type | Notes |
|-------|------|-------|
| employee_id | UUID | |
| period | varchar(7) | '2026-05' |
| total_working_days | integer | Based on shift and calendar |
| total_present_days | decimal(4,1) | Including half-days |
| total_absent_days | decimal(4,1) | |
| lop_days | decimal(4,1) | Unexcused absences |
| late_deduction_days | decimal(3,1) | From late policy |
| approved_overtime_minutes | integer | |
| overtime_multiplier_details | jsonb | Breakdown by multiplier rate |
| total_work_minutes | integer | |

**attendance_period_lock table:**
| Field | Type | Notes |
|-------|------|-------|
| lock_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| period_start | date | |
| period_end | date | |
| is_locked | boolean | |
| locked_by | UUID | HR Officer |
| locked_at | timestamptz | |
| unlocked_by | UUID | Nullable |
| unlocked_at | timestamptz | Nullable |

## 8. UI/UX Notes (Notion-like)
- Provide an "Attendance Lock" action button on the payroll preparation page with a confirmation modal.
- The lock status should be displayed as a prominent banner on the attendance pages during the locked period (e.g., "Attendance locked for May 2026 payroll").
- The reconciliation view should be a side-by-side Notion-style table: left side shows attendance summary, right side shows payroll inputs, with highlighted cells for mismatches.
- Include a "Refresh from Attendance" button on the payroll run page to re-pull attendance data.
- On mobile, the reconciliation view should stack vertically (attendance summary on top, payroll inputs below).
- Use a timeline/stepper component showing the payroll process steps: Lock Attendance -> Generate Payroll -> Review -> Finalize -> Publish.

## 9. Dependencies
- US-ATT-007: Monthly attendance summary provides the data for payroll integration.
- US-ATT-006: Approved overtime records feed into overtime pay.
- US-ATT-008: Late deduction days feed into LOP.
- Payroll module: Consumes attendance data for salary computation.
- Leave Management module: Approved leave days offset absences.
- Core HR module: Employee salary structure and employment status.

## 10. Assumptions & Constraints
- The attendance-to-payroll integration is internal (same database, same tenant context); no external API is needed.
- The payroll module calls the attendance service/repository directly within the same application boundary.
- The attendance lock is a logical lock (database flag), not a physical database lock.
- Attendance data immutability after payroll finalization is enforced at the application level.
- Multi-tenant RLS ensures the payroll module only accesses attendance data for the current tenant.
- The payroll cutoff date is tenant-configurable (default: last day of the month).

## 11. Test Hints
- Test payroll data pull: generate attendance summary, initiate payroll, verify correct data is pulled.
- Test LOP calculation: 2 absent days, no leave, verify lop_days = 2 and deduction amount is correct.
- Test overtime in payroll: 10 hours approved OT at 1.5x, verify overtime pay calculation.
- Test attendance lock: lock period, attempt clock-in/regularization, verify they are blocked.
- Test unlock and re-lock: unlock, modify attendance, re-lock, verify payroll recalculates.
- Test reconciliation view: modify attendance after payroll pull, verify mismatch is highlighted.
- Test with terminated employee: verify attendance data is included only up to last working day.
- Test payroll cutoff: set cutoff to 25th, verify only attendance from 26th (prev month) to 25th (current) is included.
- Test multi-tenant isolation: verify payroll in Tenant A cannot access Tenant B's attendance data.
- Test large dataset performance: 5,000 employees, verify API response within 5 seconds.
