---
id: US-ATT-006
module: Attendance
priority: Should Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-006: Overtime Tracking and Approval

## 1. Description
**As an** Employee,
**I want to** have my overtime hours automatically tracked and submitted for approval,
**So that** I receive proper compensation or time-off-in-lieu for the extra hours I work.

## 2. Preconditions
- Employee must be authenticated with a valid JWT session.
- Employee must have the `Attendance.Clock.Self` permission.
- The Attendance module must be enabled for the tenant.
- Overtime rules must be configured for the tenant.
- The employee must have completed a clock-in/out cycle where total hours exceed the shift's standard hours.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee clocks out and total hours exceed the shift standard by more than the overtime threshold | System processes the clock-out | An overtime record is automatically created with the excess minutes and status "Pending Approval" |
| AC-2 | Tenant policy requires pre-approval for overtime | Employee wants to work overtime | The employee must submit an overtime pre-approval request before starting; overtime without pre-approval is flagged as "Unapproved" |
| AC-3 | Manager views the overtime approval queue | Manager navigates to overtime approvals | All pending overtime records for the manager's team are displayed with employee name, date, overtime hours, and reason |
| AC-4 | Manager approves an overtime record | Manager clicks "Approve" | The overtime status is updated to "Approved" and the record is flagged for payroll integration |
| AC-5 | HR Officer views the monthly overtime report | HR navigates to the overtime report | The system displays a summary of all approved, pending, and rejected overtime by employee for the selected month |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall automatically detect overtime when `total_work_minutes` exceeds the shift's `standard_hours + overtime_threshold` (tenant-configurable, default 30 minutes).
- FR-2: The system shall create an overtime record with `employee_id`, `date`, `overtime_minutes`, `type` (auto-detected or pre-approved), and `status`.
- FR-3: The system shall support tenant-configurable overtime rules: multiplier rates (1.5x, 2x), maximum daily overtime, maximum weekly overtime, and weekend/holiday multipliers.
- FR-4: The system shall support pre-approval workflow for overtime when the tenant policy requires it.
- FR-5: The system shall route overtime records for manager approval via the Approval Workflow Engine.
- FR-6: The system shall allow managers to approve, reject, or adjust overtime hours.
- FR-7: Approved overtime records shall be flagged as payroll-ready for integration with the payroll module.
- FR-8: The system shall cap overtime at the tenant's configured maximum (daily and weekly) and alert HR if exceeded.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Overtime detection must be processed as part of the clock-out transaction (no additional API call needed).
- NFR-2: PostgreSQL RLS must enforce tenant isolation on overtime records.
- NFR-3: Overtime calculations must be deterministic and auditable (the exact formula and inputs must be logged).
- NFR-4: The overtime approval queue must load within 2 seconds at P95.

## 6. Business Rules
- BR-1: Overtime is only recognized when total work hours exceed the shift's standard hours plus the overtime threshold.
- BR-2: The overtime threshold is tenant-configurable (default: 30 minutes). Work exceeding standard hours by less than the threshold is not counted as overtime.
- BR-3: Overtime multiplier rates are tenant-configurable: weekday (default 1.5x), weekend (default 2.0x), public holiday (default 2.5x).
- BR-4: Maximum daily overtime is tenant-configurable (default: 4 hours). Overtime beyond this is capped and flagged.
- BR-5: Maximum weekly overtime is tenant-configurable (default: 20 hours). An alert is sent to HR when an employee approaches the limit.
- BR-6: If the tenant policy requires pre-approval, overtime without pre-approval is recorded but marked as "Unapproved" and excluded from payroll until HR reviews it.
- BR-7: Overtime on rest days or public holidays may have different multiplier rates.
- BR-8: Managers cannot approve their own overtime; it must route to their supervisor or HR.

## 7. Data Requirements
**overtime_record table:**
| Field | Type | Notes |
|-------|------|-------|
| overtime_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| employee_id | UUID | FK |
| attendance_log_id | UUID | FK |
| date | date | The overtime date |
| overtime_minutes | integer | Actual overtime duration |
| approved_minutes | integer | Nullable, set on approval (may differ from requested) |
| multiplier | decimal(3,2) | e.g., 1.50, 2.00 |
| type | varchar(20) | 'AUTO_DETECTED', 'PRE_APPROVED' |
| status | varchar(20) | 'PENDING', 'APPROVED', 'REJECTED', 'UNAPPROVED' |
| reason | text | Employee or system reason |
| manager_comment | text | Nullable |
| workflow_instance_id | UUID | FK to workflow engine |
| created_at / updated_at | timestamptz | Audit |
| created_by / updated_by | UUID | Audit |

## 8. UI/UX Notes (Notion-like)
- Overtime should appear as a distinct section on the employee's daily attendance detail, showing hours and multiplier.
- The manager's approval queue should show overtime requests in the same unified approval hub as regularization requests, with a filter/tab to separate them.
- Use color-coded tags: amber for "Pending," green for "Approved," red for "Rejected," gray for "Unapproved."
- The monthly overtime report should be a Notion-style table with sortable columns and an export button.
- For pre-approval, provide a simple form: date, expected overtime hours, reason.
- On mobile, overtime details should be visible on the daily attendance card with a collapsible detail section.
- Show a progress bar for weekly overtime approaching the maximum limit.

## 9. Dependencies
- US-ATT-002: Clock-out triggers overtime detection.
- US-ATT-005: Shift standard hours define the baseline for overtime calculation.
- US-ATT-009: Approved overtime feeds into payroll calculations.
- Approval Workflow Engine (technical document S34): Drives the overtime approval flow.
- Notification System: Alerts managers of pending overtime and employees of approval/rejection.

## 10. Assumptions & Constraints
- Overtime is calculated per day, not accumulated across multiple clock-in/out sessions within a day (single session model in Phase 1).
- The overtime multiplier is applied during payroll processing, not during attendance recording.
- Overtime pre-approval is optional per tenant; most tenants use auto-detection.
- The system does not enforce labor law compliance (e.g., mandatory rest periods); this is the tenant's responsibility.
- Multi-tenant RLS ensures overtime records are isolated per tenant.

## 11. Test Hints
- Test auto-detection: work 9 hours on an 8-hour shift with 30-minute threshold, verify overtime = 30 minutes.
- Test threshold: work 8 hours 20 minutes on an 8-hour shift with 30-minute threshold, verify no overtime is created.
- Test weekend multiplier: work overtime on a Saturday, verify multiplier is 2.0x (default).
- Test daily cap: work 14 hours on an 8-hour shift with 4-hour daily cap, verify overtime is capped at 4 hours.
- Test weekly cap: accumulate 21 hours of overtime in a week with 20-hour cap, verify alert to HR.
- Test pre-approval flow: enable pre-approval policy, work overtime without pre-approval, verify "Unapproved" status.
- Test manager approval: approve overtime, verify status changes and record is payroll-ready.
- Test manager adjustment: manager approves but reduces overtime from 3 hours to 2 hours, verify approved_minutes = 120.
- Test self-approval prevention: manager works overtime, verify it routes to their supervisor.
- Test multi-tenant isolation: verify overtime records are tenant-scoped via RLS.
