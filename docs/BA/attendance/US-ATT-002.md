---
id: US-ATT-002
module: Attendance
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-002: Employee Clock-Out with Work Hours Auto-Calculation

## 1. Description
**As an** Employee,
**I want to** clock out from my browser and have my work hours automatically calculated,
**So that** my daily working hours are accurately recorded without manual computation.

## 2. Preconditions
- Employee must be authenticated with a valid JWT session.
- Employee must have the `Attendance.Clock.Self` permission.
- Employee must have an open (un-clocked-out) attendance record for the current day.
- The Attendance module must be enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee has an open clock-in record for today | Employee clicks the "Clock Out" button | The `attendance_log.clock_out` field is updated with the current UTC timestamp and the total work hours are calculated and displayed (e.g., "7h 45m") |
| AC-2 | Employee has no open clock-in record for today | Employee attempts to clock out | The system displays an error: "No active clock-in found. Please clock in first or submit a regularization request." |
| AC-3 | Employee clocks out and the total hours exceed the shift's standard hours | Employee completes clock-out | The system flags the excess hours as potential overtime and stores the overtime duration separately |
| AC-4 | Employee clocks out and the total hours are below the shift's minimum hours | Employee completes clock-out | The system flags the record as "short day" for HR review |
| AC-5 | Tenant policy requires geolocation on clock-out | Employee clicks "Clock Out" | The browser captures geolocation (if permitted and required) and stores it in `clock_out_latitude` and `clock_out_longitude` |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall update the existing `attendance_log` record by setting `clock_out` to the current UTC timestamp.
- FR-2: The system shall calculate `total_work_minutes` as the difference between `clock_out` and `clock_in`, excluding any configured break deduction.
- FR-3: If the tenant has an auto-deduct break policy (e.g., 60 minutes for shifts > 6 hours), the system shall subtract the break duration from total work minutes.
- FR-4: The system shall compare total work minutes against the assigned shift's standard hours and flag overtime or short-day accordingly.
- FR-5: The system shall update the Redis cache key `att:{tenant_id}:{employee_id}:{date}` with the clock-out status and total hours.
- FR-6: The system shall optionally capture geolocation on clock-out if the tenant policy requires it.
- FR-7: If `clock_out` minus `clock_in` exceeds 16 hours, the system shall flag the record for review as a potential anomaly.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Clock-out API response time must be <= 500ms at P95.
- NFR-2: Work hours calculation must be accurate to the minute.
- NFR-3: The clock-out operation must be atomic (no partial updates if the request fails mid-way).
- NFR-4: PostgreSQL RLS must enforce tenant isolation on the `attendance_log` table.
- NFR-5: The UI must handle timezone display correctly, showing times in the employee's location timezone.

## 6. Business Rules
- BR-1: Clock-out is only allowed if there is an active (open) clock-in record for the employee.
- BR-2: Total work hours = (clock_out - clock_in) - auto_break_deduction (if applicable).
- BR-3: If total work hours exceed the shift's standard hours by more than the tenant's overtime threshold, the excess is classified as overtime pending approval.
- BR-4: If total work hours are less than the shift's minimum required hours, the record is marked as "short day."
- BR-5: An auto-clock-out Hangfire job should run at the end of day (e.g., 11:59 PM tenant timezone) to close any unclosed records with a system-generated clock-out and flag them for regularization.
- BR-6: The maximum allowed duration for a single clock-in/out session is 16 hours; anything beyond is flagged as anomalous.

## 7. Data Requirements
**Input:**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| latitude | decimal(10,7) | Conditional | If tenant geo policy applies to clock-out |
| longitude | decimal(10,7) | Conditional | If tenant geo policy applies to clock-out |

**Updated fields on attendance_log:**
| Field | Type | Notes |
|-------|------|-------|
| clock_out | timestamptz | UTC |
| clock_out_latitude | decimal(10,7) | Nullable |
| clock_out_longitude | decimal(10,7) | Nullable |
| clock_out_ip | varchar(50) | Source IP |
| total_work_minutes | integer | Computed: clock_out - clock_in - break |
| overtime_minutes | integer | Nullable, if exceeds shift standard |
| status | varchar(20) | 'COMPLETE', 'SHORT_DAY', 'OVERTIME', 'ANOMALY' |
| updated_at | timestamptz | Audit |
| updated_by | UUID | Audit |

## 8. UI/UX Notes (Notion-like)
- When clocked in, the dashboard card should show a live elapsed timer (updated every second via client-side JS, not API polling).
- The "Clock Out" button replaces the "Clock In" button on the dashboard card with a distinct color (e.g., warm red/orange vs. green for clock-in).
- On clock-out, display a brief summary card: clock-in time, clock-out time, total hours, and overtime (if any) with smooth fade-in animation.
- If short day or overtime is detected, show a subtle badge/tag on the summary card.
- Mobile: ensure the clock-out button and summary are fully visible without scrolling on small screens.
- Use Notion-style inline status pills (e.g., green "Complete", amber "Short Day", blue "Overtime").

## 9. Dependencies
- US-ATT-001: Clock-in must exist before clock-out.
- US-ATT-005: Shift definition provides standard hours and break rules for calculation.
- US-ATT-006: Overtime records feed into the overtime tracking and approval workflow.
- Redis infrastructure for caching daily attendance status.

## 10. Assumptions & Constraints
- Break deduction is configured at the tenant or shift level, not manually entered by the employee.
- The system uses server-side UTC time for clock-out, not client-reported time, to prevent tampering.
- The auto-clock-out Hangfire job is a safety net; employees are expected to clock out manually.
- The "total_work_minutes" calculation does not account for multiple clock-in/out sessions in a single day (single session model in Phase 1).
- Multi-tenant RLS ensures clock-out operations are scoped to the employee's tenant.

## 11. Test Hints
- Test normal clock-out flow: clock in, wait, clock out, verify total hours are correct.
- Test clock-out without prior clock-in: verify error message.
- Test auto-break deduction: configure a 60-minute break for 8-hour shifts, verify deduction.
- Test overtime detection: clock in for 10 hours on an 8-hour shift, verify overtime_minutes = 120.
- Test short-day detection: clock in for 4 hours on an 8-hour shift, verify "short day" status.
- Test anomaly detection: simulate a clock-in/out span > 16 hours, verify anomaly flag.
- Test the auto-clock-out Hangfire job: leave a record open past midnight, verify system closes it.
- Test multi-tenant isolation: verify employee in Tenant A cannot clock out on Tenant B's record.
- Test geolocation capture on clock-out when tenant policy requires it.
