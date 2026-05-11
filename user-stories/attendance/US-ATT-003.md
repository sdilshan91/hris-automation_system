---
id: US-ATT-003
module: Attendance
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-003: Attendance Regularization Request (Forgot Clock-In/Out)

## 1. Description
**As an** Employee,
**I want to** submit an attendance regularization request when I forgot to clock in or clock out,
**So that** my attendance record is corrected and my working hours are accurately reflected for payroll.

## 2. Preconditions
- Employee must be authenticated with a valid JWT session.
- Employee must have the `Attendance.Regularize.Self` permission.
- The Attendance module must be enabled for the tenant.
- A regularization approval workflow must be configured for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee missed clock-in for a past working day | Employee submits a regularization request with the missed clock-in time and reason | A new `attendance_regularization` record is created with status "Pending" and a workflow instance is initiated for manager approval |
| AC-2 | Employee clocked in but forgot to clock out | Employee submits a regularization request with the missing clock-out time and reason | The system creates a regularization record linked to the existing `attendance_log` entry, with status "Pending" |
| AC-3 | Employee attempts to submit a regularization request for a date older than the tenant's allowed lookback period (e.g., 7 days) | Employee submits the request | The system rejects the request with: "Regularization requests can only be submitted for the last {N} days." |
| AC-4 | Employee submits a regularization request for a date that already has a pending regularization | Employee submits the request | The system rejects the request with: "A pending regularization request already exists for this date." |
| AC-5 | Employee submits a regularization request for a date that falls within a locked payroll period | Employee submits the request | The system rejects the request with: "This date falls within a locked payroll period. Please contact HR." |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide a form allowing the employee to select the date, regularization type (missed clock-in, missed clock-out, or both), the corrected time(s), and a reason.
- FR-2: The system shall create an `attendance_regularization` record with `tenant_id`, `employee_id`, `date`, `regularization_type`, `requested_clock_in`, `requested_clock_out`, `reason`, and `status`.
- FR-3: The system shall initiate the tenant's configured approval workflow for attendance regularization upon submission.
- FR-4: The system shall send an in-app notification (and optionally email) to the approver (line manager) when a regularization request is submitted.
- FR-5: The system shall validate that the requested times are logically consistent (clock-in before clock-out, within a single calendar day, not in the future).
- FR-6: The system shall enforce a tenant-configurable lookback period (default: 7 days) for regularization requests.
- FR-7: The system shall prevent regularization for dates within a locked payroll period.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Regularization submission API response time must be <= 500ms at P95.
- NFR-2: PostgreSQL RLS must enforce tenant isolation on the `attendance_regularization` table.
- NFR-3: All regularization actions (submit, approve, reject) must be recorded in the audit log.
- NFR-4: The regularization form must be accessible and responsive on mobile devices (360px minimum width).

## 6. Business Rules
- BR-1: Regularization requests require at least one level of approval (configurable via the Approval Workflow Engine).
- BR-2: The lookback period for regularization is tenant-configurable (default: 7 calendar days).
- BR-3: Only one pending regularization request is allowed per employee per date.
- BR-4: Regularization requests cannot be submitted for future dates.
- BR-5: If the regularized date has an existing attendance_log record (e.g., clocked in but not out), the regularization updates that record upon approval. If no record exists, a new attendance_log is created upon approval.
- BR-6: Regularization requests cannot modify records within a locked payroll period unless HR manually unlocks the period.
- BR-7: The reason field is mandatory and must be at least 10 characters.

## 7. Data Requirements
**Input:**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| date | date | Yes | The date to regularize |
| regularization_type | enum | Yes | 'MISSED_CLOCK_IN', 'MISSED_CLOCK_OUT', 'MISSED_BOTH' |
| requested_clock_in | time | Conditional | Required if type is MISSED_CLOCK_IN or MISSED_BOTH |
| requested_clock_out | time | Conditional | Required if type is MISSED_CLOCK_OUT or MISSED_BOTH |
| reason | text | Yes | Minimum 10 characters |

**attendance_regularization record:**
| Field | Type | Notes |
|-------|------|-------|
| regularization_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| employee_id | UUID | FK |
| attendance_log_id | UUID | FK, nullable (null if no existing record) |
| date | date | The regularized date |
| regularization_type | varchar(20) | Enum |
| requested_clock_in | timestamptz | Nullable |
| requested_clock_out | timestamptz | Nullable |
| reason | text | Mandatory |
| status | varchar(20) | 'PENDING', 'APPROVED', 'REJECTED', 'CANCELLED' |
| workflow_instance_id | UUID | FK to workflow engine |
| created_at | timestamptz | Audit |
| created_by | UUID | Audit |
| updated_at | timestamptz | Audit |
| updated_by | UUID | Audit |

## 8. UI/UX Notes (Notion-like)
- Provide a "Request Regularization" button on the attendance history page next to dates with missing or incomplete records.
- The regularization form should be a Notion-style modal/drawer that slides in from the right.
- Pre-populate the date and show the existing attendance data (if any) for context.
- Use inline validation for the reason field (show character count, highlight if below minimum).
- After submission, show a Notion-style status pill ("Pending") next to the date in the attendance history.
- On mobile, the form should be full-screen to maximize input area.
- Show the approval chain (who will approve) below the form before submission.

## 9. Dependencies
- US-ATT-001 / US-ATT-002: Attendance log records that may need regularization.
- US-ATT-004: Manager approval of regularization requests.
- Approval Workflow Engine (technical document S34): Workflow definition for attendance regularization.
- Notification System: In-app and email notifications to approvers.
- Payroll module: Payroll period lock status to prevent regularization of locked periods.

## 10. Assumptions & Constraints
- The approval workflow for regularization is pre-configured by the Tenant Admin.
- The default workflow is single-level (line manager approval); multi-level is configurable.
- Regularization does not create a new attendance_log record until it is approved (see US-ATT-004).
- The system does not support partial-day regularization (e.g., adjusting only one hour); it replaces the entire clock-in or clock-out time.
- Multi-tenant RLS ensures regularization records are isolated per tenant.
- The lookback period is a soft limit configurable by Tenant Admin; HR can override via direct record adjustment.

## 11. Test Hints
- Test submission for a date with no attendance record (missed both): verify regularization record is created.
- Test submission for a date with clock-in but no clock-out: verify type is MISSED_CLOCK_OUT.
- Test lookback period enforcement: submit for a date beyond the allowed period, verify rejection.
- Test duplicate prevention: submit two regularization requests for the same date, verify second is rejected.
- Test payroll lock enforcement: lock a payroll period, then attempt regularization for a date in that period.
- Test validation: submit with reason shorter than 10 characters, verify rejection.
- Test notification: verify manager receives in-app notification upon submission.
- Test multi-tenant isolation: verify employee cannot see or submit regularization for another tenant.
- Test that clock-in time must be before clock-out time when both are provided.
