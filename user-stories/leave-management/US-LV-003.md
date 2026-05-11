---
id: US-LV-003
module: Leave Management
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-LV-003: Employee Applies for Leave

## 1. Description
**As an** Employee,
**I want to** apply for leave by selecting a leave type, start/end dates, and providing a reason,
**So that** my manager can review and approve my time off request.

## 2. Preconditions
- Employee is authenticated and has an active employee record in the tenant.
- At least one active leave type is configured for the tenant (US-LV-001).
- Leave entitlements have been calculated for the current leave year (US-LV-002).
- Employee has `Leave.Apply` permission (default for Employee role).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee is on the Leave Application page | They select a leave type, start date, end date, and enter a reason, then submit | A leave request is created with status "Pending", a `leave-requested` notification is queued to the manager, and the employee sees a confirmation |
| AC-2 | Employee selects a leave type and date range | The system validates the request | Available balance is shown inline; if balance is insufficient (and negative balance is not allowed), submission is blocked with a clear message |
| AC-3 | Employee applies for Sick Leave for more than the document threshold days | They submit the form without attaching a document | Validation error: "Medical certificate is required for sick leave exceeding N days" |
| AC-4 | Employee selects a half-day leave | They toggle the half-day option and select AM/PM session | The request is created for 0.5 days and the balance is decremented accordingly |
| AC-5 | Employee's selected dates overlap with an existing approved/pending leave request | They submit the form | Validation error: "You already have a leave request for the selected dates" |
| AC-6 | Employee applies for leave during a public holiday | The system validates the request | Public holidays are excluded from the leave day count automatically; the employee is informed of the adjusted day count |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: Leave application form with fields: leave type (dropdown), start date (date picker), end date (date picker), half-day toggle (AM/PM), reason (text area), attachment (file upload, optional/required per leave type config).
- FR-2: Real-time balance display: When employee selects a leave type, show current balance, requested days, and projected remaining balance.
- FR-3: Working days calculation: Exclude weekends (configurable per tenant: 5-day or 6-day work week) and public holidays from the leave day count.
- FR-4: Overlap detection: Check against existing Pending/Approved leave requests for the same employee.
- FR-5: API endpoint: `POST /api/v1/leaves` with request body containing `leaveTypeId`, `startDate`, `endDate`, `isHalfDay`, `halfDaySession`, `reason`, `attachments[]`.
- FR-6: On successful submission, insert into `leave_request` table with `status = 'Pending'` and queue notification via the notification service.
- FR-7: Support multi-level approval routing based on tenant workflow configuration.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Leave application submission API must respond within 500ms (P95).
- NFR-2: Balance check must use Redis-cached values; fallback to DB if cache miss.
- NFR-3: File attachments must be stored in tenant-scoped blob storage path `{tenantId}/leaves/{requestId}/`.
- NFR-4: All operations tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-5: Form must be usable on mobile devices (360px+) with touch-friendly date pickers.

## 6. Business Rules
- BR-1: Leave requests cannot be submitted for past dates beyond a configurable lookback window (e.g., 7 days; tenant-configurable).
- BR-2: Leave requests cannot be submitted for dates beyond a configurable future window (e.g., 90 days ahead; tenant-configurable).
- BR-3: Maximum consecutive leave days are enforced per leave type configuration.
- BR-4: Gender-restricted leave types are only shown to eligible employees.
- BR-5: Employees on probation can only see/apply for leave types marked `probation_eligible`.
- BR-6: The manager/approver is determined by the employee's reporting line in Core HR (manager_employee_id field).

## 7. Data Requirements
- **Table:** `leave_request`
- **Key columns:** `leave_request_id (uuid PK)`, `tenant_id (uuid FK)`, `employee_id (uuid FK)`, `leave_type_id (uuid FK)`, `start_date (date)`, `end_date (date)`, `is_half_day (boolean)`, `half_day_session (varchar(2))`, `total_days (numeric(5,2))`, `reason (text)`, `status (varchar(20))` [Pending, Approved, Rejected, Cancelled], `requested_at (timestamptz)`, `attachment_urls (text[])`, audit columns.
- **Index:** `leave_request(tenant_id, employee_id, status, start_date)` as defined in the technical document.
- **Partial index:** `ix_leave_pending ON leave_request(tenant_id, start_date) WHERE status = 'Pending'`.

## 8. UI/UX Notes (Notion-like)
- Clean, minimal form with generous whitespace; Notion-style inline property editor feel.
- Leave type dropdown with color-coded badges and remaining balance shown next to each type.
- Date range picker with visual calendar showing existing leave (colored blocks) and holidays (gray blocks).
- Real-time "days calculated" chip updates as dates change, excluding holidays/weekends.
- Drag-and-drop file upload area with progress indicator.
- Success state: Subtle green toast notification with request ID; page transitions to "My Leaves" list.
- Mobile: Full-screen form with sticky submit button at bottom.

## 9. Dependencies
- **US-LV-001**: Leave types must be configured.
- **US-LV-002**: Entitlements and balances must be calculated.
- **US-LV-007**: Holiday calendar must be set up for working-day calculations.
- **US-CORE-***: Employee record with reporting manager must exist.
- **US-AUTH-***: JWT authentication with tenant context.
- **Notification Service**: For queuing `leave-requested` event to manager.

## 10. Assumptions & Constraints
- The tenant's work-week configuration (5-day or 6-day) is available from tenant settings.
- File uploads are limited to 5MB per file, max 3 files per request (configurable).
- Supported file types: PDF, JPG, PNG.
- The notification service is asynchronous (fire-and-forget from the API's perspective).

## 11. Test Hints
- Test balance validation: Apply for more days than available; verify rejection.
- Test overlap detection: Create two overlapping leave requests; verify the second is rejected.
- Test holiday exclusion: Apply for Mon-Fri where Wednesday is a holiday; verify total_days = 4.
- Test half-day: Apply for a half-day and verify 0.5 day deduction.
- Test document requirement: Apply for sick leave exceeding threshold without attachment; verify error.
- Test past-date restriction: Apply for a date 30 days in the past with a 7-day lookback; verify rejection.
- Test gender filter: Verify male employee cannot see Maternity leave type.
- Test tenant isolation: Verify employee in Tenant A cannot submit leave via Tenant B's API.
