---
id: US-PAY-010
module: Payroll
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-010: Attendance and Leave Data Integration into Payroll

## 1. Description
**As an** HR Officer,
**I want** the payroll system to automatically integrate attendance and leave data when calculating salaries,
**So that** Loss of Pay (LOP), overtime earnings, and leave encashment are accurately reflected in payslips without manual data entry.

## 2. Preconditions
- Attendance module is operational and attendance data for the payroll period is finalized (technical doc section 11.5).
- Leave module is operational and leave requests for the payroll period are approved/rejected (technical doc section 11.4).
- Salary structures with LOP and overtime components are configured (US-PAY-001).
- Employee shift assignments exist in the attendance module.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee X was absent for 3 days in May 2026 without approved leave | The May 2026 payroll run executes | Employee X's payslip shows 3 LOP days; the LOP deduction = (monthly_basic / working_days) * 3; net salary is reduced accordingly |
| AC-2 | Employee Y worked 10 hours of approved overtime in May 2026 | The May 2026 payroll run executes | Employee Y's payslip includes an overtime earning component calculated per the tenant's OT rate configuration (e.g., 1.5x hourly rate) |
| AC-3 | Employee Z has 5 days of unused leave eligible for encashment at year-end | HR triggers leave encashment processing | The encashment amount (5 * daily_basic) is added as an earning adjustment to the next payroll run |
| AC-4 | Attendance data for May 2026 is NOT finalized | HR attempts to run payroll for May 2026 | The system blocks the payroll run with a warning: "Attendance data for May 2026 is not yet finalized" |
| AC-5 | Attendance and leave data is tenant-scoped | The payroll engine fetches attendance data | Only attendance and leave records matching the payroll run's tenant_id are retrieved; RLS enforces isolation |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The payroll engine SHALL fetch the monthly attendance summary per employee from the attendance module, including: total working days, present days, absent days, half-days, late arrivals, overtime hours.
- FR-2: The payroll engine SHALL fetch approved leave records for the period from the leave module, including: leave type, duration (full/half day), and paid/unpaid classification.
- FR-3: The system SHALL calculate LOP as: absent days (without approved leave or with unpaid leave) multiplied by daily rate. Daily rate = monthly_basic / total_working_days_in_month (per technical doc section 11.8).
- FR-4: The system SHALL calculate overtime earnings based on tenant-configured OT rules: OT rate multiplier (e.g., 1.5x, 2x for holidays), applicable hours, and the base hourly rate derivation.
- FR-5: The system SHALL support leave encashment calculation: number of eligible days multiplied by daily rate, triggered manually by HR or automatically at fiscal year-end.
- FR-6: The system SHALL lock attendance and leave records for the payroll period when a payroll run transitions to Processing status (per technical doc section 16.4), preventing modifications that would invalidate the calculation.
- FR-7: The system SHALL provide a pre-payroll attendance reconciliation report showing per-employee: working days, present days, leave days (by type), absent days, overtime hours, and calculated LOP days.
- FR-8: All cross-module data access SHALL be tenant-scoped via `ITenantContext` and RLS.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Attendance and leave data fetch for 5,000 employees SHALL complete within 2 minutes (as part of the overall 10-minute payroll run NFR).
- NFR-2: The attendance lock mechanism SHALL be advisory (application-level flag) not a database-level lock, to avoid long-running transactions.
- NFR-3: LOP and overtime calculation logic SHALL have >= 85% test coverage.
- NFR-4: Cross-module data access SHALL use internal service interfaces (not HTTP calls) for performance, as all modules run in the same process.
- NFR-5: The pre-payroll reconciliation report SHALL generate within 30 seconds for 5,000 employees.

## 6. Business Rules
- BR-1: LOP is calculated only for days that are both absent AND do not have approved paid leave.
- BR-2: Half-day absence counts as 0.5 LOP days.
- BR-3: Late arrivals beyond a tenant-configured threshold (e.g., 3 lates = 0.5 day LOP) are converted to LOP according to the tenant's late-deduction policy.
- BR-4: Overtime must be pre-approved by the employee's manager to be eligible for overtime earnings. Unapproved overtime is excluded.
- BR-5: Public holidays worked count as overtime at the holiday OT rate (typically 2x).
- BR-6: Leave encashment is only available for leave types that have encashment enabled and only for the balance exceeding the carry-forward limit.
- BR-7: Employees on notice period may have different LOP rules as per tenant policy (e.g., absence during notice period = 2x LOP deduction).
- BR-8: Working days in a month are determined by the employee's shift calendar, not a flat 30 days; this accounts for different shift patterns across departments.
- BR-9: Attendance regularization requests that are approved after the payroll lock must be processed as corrections in the next payroll run.

## 7. Data Requirements

**Attendance Summary (consumed by payroll engine):**
| Field | Type | Source |
|-------|------|--------|
| employee_id | uuid | Attendance module |
| tenant_id | uuid | Attendance module |
| pay_month | int | Derived |
| pay_year | int | Derived |
| total_working_days | numeric(5,2) | Shift calendar |
| present_days | numeric(5,2) | Attendance logs |
| absent_days | numeric(5,2) | Computed |
| half_days | numeric(5,2) | Attendance logs |
| late_count | int | Attendance logs |
| overtime_hours | numeric(7,2) | Attendance logs |
| is_finalized | boolean | Attendance module |

**Leave Summary (consumed by payroll engine):**
| Field | Type | Source |
|-------|------|--------|
| employee_id | uuid | Leave module |
| tenant_id | uuid | Leave module |
| leave_type | varchar(50) | Leave module |
| is_paid | boolean | Leave type config |
| total_days | numeric(5,2) | Leave requests |

**payroll_slip additions (from US-PAY-003, enriched):**
| Column | Type | Notes |
|--------|------|-------|
| lop_days | numeric(5,2) | From attendance integration |
| overtime_hours | numeric(7,2) | From attendance integration |
| overtime_amount | numeric(18,2) | Calculated |
| leave_encashment_days | numeric(5,2) | If applicable |
| leave_encashment_amount | numeric(18,2) | If applicable |

## 8. UI/UX Notes (Notion-like)
- Pre-payroll reconciliation page: a Notion-style table showing each employee's attendance summary with color-coded cells (red for absences, amber for late, green for full attendance).
- A "Reconcile" button that shows mismatches between attendance data and leave data (e.g., absent day with no leave record).
- Drill-down: clicking an employee row shows their daily attendance calendar for the month with status icons.
- Warning banner at top of payroll initiation page if attendance is not finalized, with a link to the attendance finalization page.
- Overtime hours column with a tooltip showing breakdown by date.
- Mobile: reconciliation summary viewable; detailed daily drill-down deferred to desktop.

## 9. Dependencies
- **US-ATT-xxx**: Attendance module must provide finalized monthly attendance summary API.
- **US-LEAVE-xxx**: Leave module must provide approved leave summary API for the period.
- **US-PAY-001**: LOP and Overtime salary components must be configured.
- **US-PAY-003**: Payroll run engine must call the attendance/leave integration service.
- Shift calendar configuration must be complete for accurate working-day calculation.

## 10. Assumptions & Constraints
- Attendance and leave modules are part of the same application process (monolith in Phase 1); data access is via internal service interfaces, not HTTP API calls.
- Attendance finalization is a manual step performed by HR before payroll initiation; auto-finalization at a configurable cutoff date is a Phase 2 enhancement.
- The payroll lock on attendance/leave is advisory and reversible by Tenant Admin if the payroll run is cancelled.
- Overtime calculation rules (rate multipliers, eligible hours) are tenant-configurable and not hardcoded.

## 11. Test Hints
- Unit test: Verify LOP calculation for 3 absent days in a month with 22 working days and monthly basic of 22,000 yields LOP of 3,000.
- Unit test: Verify half-day absence counts as 0.5 LOP days.
- Unit test: Verify late-to-LOP conversion: 6 lates with policy "3 lates = 0.5 LOP" yields 1.0 LOP day.
- Unit test: Verify overtime calculation: 10 hours at 1.5x rate with hourly rate of 200 yields 3,000.
- Unit test: Verify leave encashment for 5 days at daily rate of 1,000 yields 5,000.
- Integration test: Finalize attendance, run payroll, verify LOP and overtime values match attendance summary.
- Integration test: Attempt payroll run with unfinalized attendance and verify blocking error.
- Integration test: Verify attendance lock is applied during payroll Processing status and released on cancellation.
- E2E (Playwright): View pre-payroll reconciliation, verify attendance data displays, initiate payroll, verify LOP appears on payslip.
