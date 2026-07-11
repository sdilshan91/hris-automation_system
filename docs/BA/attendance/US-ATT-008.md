---
id: US-ATT-008
module: Attendance
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-008: Late Arrival and Early Departure Tracking

## 1. Description
**As an** HR Officer,
**I want to** automatically track late arrivals and early departures based on shift schedules,
**So that** I can identify attendance patterns, enforce policies, and provide accurate data for payroll deductions if applicable.

## 2. Preconditions
- The Attendance module must be enabled for the tenant.
- Employees must have assigned shifts with defined start/end times (US-ATT-005).
- Grace period must be configured at the shift or tenant level.
- Clock-in/out records must exist (US-ATT-001/US-ATT-002).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee's shift starts at 09:00 with a 15-minute grace period | Employee clocks in at 09:20 | The system marks the attendance record as "Late" with `late_minutes = 20` and records `late_by = 5` (minutes after grace) |
| AC-2 | Employee's shift starts at 09:00 with a 15-minute grace period | Employee clocks in at 09:10 | The clock-in is accepted as on-time (within grace period) and no late flag is set |
| AC-3 | Employee's shift ends at 17:00 | Employee clocks out at 16:30 | The system marks the attendance record as "Early Departure" with `early_departure_minutes = 30` |
| AC-4 | Tenant policy has a late deduction rule (e.g., 3 late arrivals = 0.5 day deduction) | Employee accumulates 3 late arrivals in a month | The system flags the employee for the applicable deduction in the monthly summary and notifies the employee |
| AC-5 | Manager views the team late arrival report | Manager navigates to the team attendance report | The system displays a list of team members with their late arrival and early departure counts for the selected period |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall compare each clock-in time against the employee's assigned shift start_time + grace_period to determine lateness.
- FR-2: The system shall compare each clock-out time against the employee's assigned shift end_time to determine early departure.
- FR-3: Late arrivals and early departures shall be recorded as fields on the `attendance_log` record: `is_late` (boolean), `late_minutes` (integer), `is_early_departure` (boolean), `early_departure_minutes` (integer).
- FR-4: The system shall support tenant-configurable late arrival policies: warning threshold, deduction rules (e.g., N lates = X day deduction), and notification triggers.
- FR-5: The system shall send in-app notifications to employees when they are marked late, including the number of late arrivals in the current month.
- FR-6: The system shall provide a late/early departure report for managers (team scope) and HR (all scope) with filters for date range, department, and employee.
- FR-7: The system shall support a configurable "chronic lateness" threshold (e.g., more than 5 lates/month) that triggers an escalation notification to HR.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Late/early detection must be computed during the clock-in/clock-out transaction with no additional latency (inline calculation).
- NFR-2: PostgreSQL RLS must enforce tenant isolation on attendance records used for late/early tracking.
- NFR-3: The late/early report must load within 2 seconds at P95 for up to 500 employees.
- NFR-4: Late arrival notifications must be delivered within 1 minute of clock-in.

## 6. Business Rules
- BR-1: Late arrival = clock_in_time > (shift_start_time + grace_period_minutes).
- BR-2: Early departure = clock_out_time < shift_end_time, and the employee has not completed the shift's minimum required hours.
- BR-3: Grace period is defined at the shift level (see US-ATT-005). If not set, tenant-level default applies. If neither is set, grace period is 0.
- BR-4: Late deduction rules are tenant-configurable. Example: 3 lates = 0.5 day salary deduction; 6 lates = 1 day deduction. Deductions feed into LOP in the monthly summary.
- BR-5: Clock-ins before the shift start time (early arrival) are valid but not counted as extra hours unless the tenant policy allows it.
- BR-6: Flexible shifts (no fixed start/end) do not trigger late/early tracking; only minimum hours are enforced.
- BR-7: Regularized attendance records inherit the late/early status based on the regularized times, not the submission time.
- BR-8: Employees on approved half-day leave are evaluated against a half-day shift schedule for late/early tracking.

## 7. Data Requirements
**Fields added to attendance_log:**
| Field | Type | Notes |
|-------|------|-------|
| is_late | boolean | Default false |
| late_minutes | integer | 0 if not late |
| is_early_departure | boolean | Default false |
| early_departure_minutes | integer | 0 if not early |

**late_policy (tenant-level configuration):**
| Field | Type | Notes |
|-------|------|-------|
| policy_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| threshold_count | integer | Number of lates triggering deduction |
| deduction_days | decimal(3,1) | e.g., 0.5 |
| period | varchar(10) | 'MONTHLY', 'QUARTERLY' |
| notification_on_late | boolean | Send notification on each late |
| chronic_threshold | integer | Lates per month for HR escalation |
| is_active | boolean | |

## 8. UI/UX Notes (Notion-like)
- On the employee's daily attendance view, show a small "Late" or "Early" badge next to the clock-in/out time.
- The late badge should show the number of minutes (e.g., "Late by 12 min") in a subtle amber/red pill.
- On the monthly summary view, include a "Late Count" and "Early Departure Count" column.
- The late/early report for managers should be a Notion-style table with conditional formatting (e.g., rows highlighted amber for chronic lateness).
- Provide a visual monthly trend chart (small bar chart) showing late arrivals per employee.
- On mobile, late/early indicators should be visible on the attendance card without requiring expansion.
- Show a monthly "lateness score" or progress indicator on the employee self-service dashboard (e.g., "2 of 3 allowed lates used this month").

## 9. Dependencies
- US-ATT-001: Clock-in time is the input for late detection.
- US-ATT-002: Clock-out time is the input for early departure detection.
- US-ATT-005: Shift start/end times and grace period define the thresholds.
- US-ATT-007: Late/early counts are aggregated in the monthly summary.
- US-ATT-009: Late deductions (LOP) feed into payroll.
- Notification System: Sends late arrival notifications.

## 10. Assumptions & Constraints
- Late/early tracking is only applicable to Single and Rotating shifts, not Flexible shifts.
- Grace period applies only to late arrival, not to early departure.
- The system does not distinguish between "excused" and "unexcused" lateness in Phase 1; all lates are treated equally.
- Late deduction policies are optional; tenants may choose not to enforce deductions.
- Multi-tenant RLS ensures late tracking data is isolated per tenant.
- The system does not account for external factors (traffic, weather) in lateness evaluation; this is at the manager's discretion.

## 11. Test Hints
- Test on-time clock-in within grace: shift at 09:00, grace 15 min, clock in at 09:10, verify not late.
- Test late clock-in beyond grace: shift at 09:00, grace 15 min, clock in at 09:20, verify late_minutes = 20.
- Test early departure: shift ends 17:00, clock out at 16:30, verify early_departure_minutes = 30.
- Test late deduction rule: configure 3 lates = 0.5 day deduction, accumulate 3 lates, verify deduction flag.
- Test chronic lateness notification: configure threshold at 5, accumulate 5 lates, verify HR is notified.
- Test flexible shift exemption: assign flexible shift, clock in at any time, verify no late flag.
- Test regularized attendance: regularize a late clock-in to an on-time value, verify late flag is recalculated.
- Test half-day leave scenario: employee on half-day leave, verify late/early is evaluated against half-day schedule.
- Test multi-tenant isolation: verify late policies and records are tenant-scoped.
