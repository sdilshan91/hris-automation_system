/**
 * US-ATT-001: Attendance clock-in models matching the backend API contract.
 *
 * Backend endpoint (backend agent building in parallel -- assumed contract):
 *   GET  /api/v1/attendance/status     - current employee's clock-in status for today (IClockStatus)
 *   POST /api/v1/attendance/clock-in   - create an attendance_log clock-in, returns IAttendanceLog
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is `${apiBaseUrl}/attendance`.
 *
 * Geolocation policy (BR-2) is tenant-level: the FE reads `requireGeolocation` from the
 * status endpoint to decide whether a denied browser permission BLOCKS clock-in (AC-3) or
 * is simply omitted (AC-4). The backend remains the authority for geo-fence (FR-3) and the
 * IP allowlist (FR-4, AC-5) — those are enforced server-side and surfaced as typed errors.
 */

import type { Schema } from '@core/api';

/** Clock-in source channel recorded on the attendance log (§7 `source`). */
export type AttendanceSource = 'WEB' | 'MOBILE_WEB';

/**
 * Attendance log entity returned by the API after a successful clock-in (FR-1).
 * Geolocation fields are nullable (AC-4 — optional policy / denied permission).
 */
export interface IAttendanceLog {
  attendanceLogId: string;
  tenantId: string;
  employeeId: string;
  /** Clock-in timestamp in UTC (FR-7); the UI converts to local time for display. */
  clockIn: string;
  /** Set on clock-out; null while the record is open (BR-1). */
  clockOut: string | null;
  clockInLatitude: number | null;
  clockInLongitude: number | null;
  source: AttendanceSource;
  /**
   * US-ATT-008 (FR-3, AC-1): true when this clock-in was beyond shift start + grace.
   * Drives the amber/red "Late by {n} min" badge on the daily card (§8).
   */
  isLate?: boolean;
  /** US-ATT-008 (AC-1): minutes late past the shift start time; 0/absent when on time. */
  lateMinutes?: number;
}

/**
 * Request payload for clocking in (FR-1).
 * Coordinates are conditional (§7): required only when the tenant geo policy is
 * mandatory, otherwise omitted. The backend stamps IP + user-agent server-side (FR-5).
 */
export interface IClockInRequest {
  /** Latitude, when captured. Null/omitted when geo is optional and not granted (AC-4). */
  latitude: number | null;
  /** Longitude, when captured. Null/omitted when geo is optional and not granted (AC-4). */
  longitude: number | null;
  /** Channel the clock-in came from ('WEB' from a desktop browser). */
  source: AttendanceSource;
}

/**
 * Current clock-in status for the employee today (dashboard fast lookup, FR-6).
 * Drives the initial card state: already-clocked-in shows the live timer (AC-2 reflect),
 * and `requireGeolocation` decides the AC-3 vs AC-4 branch when permission is denied.
 */
export interface IClockStatus {
  /** True when there is an open (un-clocked-out) record for today (BR-1, AC-2). */
  isClockedIn: boolean;
  /** UTC clock-in timestamp of the open record, when `isClockedIn` is true. */
  clockedInAt: string | null;
  /** BR-2: tenant requires geolocation — a denied permission must block clock-in (AC-3). */
  requireGeolocation: boolean;
  /** Assigned shift display name for context (§8). Null when no shift is assigned yet. */
  shiftName: string | null;
  /** Expected shift start time (HH:mm, employee-local) for context (§8). Null if unknown. */
  shiftStart: string | null;
}

/**
 * Typed error body from the backend for clock-in (AC-2, AC-5).
 * `message` is shown verbatim inline; `code` is the machine-readable discriminator:
 *   - 409 `code: 'already_clocked_in'` -> "You have already clocked in..." (AC-2)
 *   - 403 `code: 'ip_not_allowed'`     -> IP allowlist rejection (AC-5)
 *   - 403 `code: 'geo_fence_violation'`-> coordinates outside the allowed radius (FR-3)
 */
export interface IClockInErrorResponse {
  message: string;
  code?: 'already_clocked_in' | 'ip_not_allowed' | 'geo_fence_violation' | string;
}

/**
 * Result of attempting to read the browser geolocation (AC-3, AC-4).
 * `denied` is true when the user refused permission or the browser blocked it;
 * `coords` carries the captured position when granted.
 */
export interface IGeolocationResult {
  granted: boolean;
  denied: boolean;
  coords: { latitude: number; longitude: number } | null;
  /** Human-readable reason when not granted (permission denied, unavailable, timeout). */
  error: string | null;
}

/**
 * US-ATT-002: Clock-out work-hours status (§7 `status`).
 *  - COMPLETE   : total hours within the shift's standard band (BR-2).
 *  - SHORT_DAY  : below the shift's minimum required hours (BR-4, AC-4) — HR review.
 *  - OVERTIME   : exceeded the shift's standard hours by the overtime threshold (BR-3, AC-3).
 *  - ANOMALY    : span > 16h (BR-6, FR-7) — flagged for review.
 */
export type ClockOutStatus = 'COMPLETE' | 'SHORT_DAY' | 'OVERTIME' | 'ANOMALY';

/**
 * Request payload for clocking out (US-ATT-002 FR-1, AC-5).
 * Coordinates are conditional (§7): sent only when the tenant geo policy requires
 * geolocation on clock-out and the browser granted permission; otherwise omitted/null.
 * The backend stamps clock_out (server UTC), IP and audit fields server-side (§10).
 */
export interface IClockOutRequest {
  /** Latitude, when captured (AC-5). Null when geo is not required or not granted. */
  latitude: number | null;
  /** Longitude, when captured (AC-5). Null when geo is not required or not granted. */
  longitude: number | null;
}

/**
 * Result of a successful clock-out (US-ATT-002 AC-1, AC-3, AC-4, FR-2/4/7).
 * Drives the summary card: clock-in/out times (UTC -> local), total hours, overtime,
 * and the status pill. The backend computes all durations and the status — the FE
 * only formats and labels them.
 */
export interface IClockOutResult {
  attendanceLogId: string;
  /** Clock-in timestamp in UTC; UI converts to the employee's local time (NFR-5). */
  clockIn: string;
  /** Clock-out timestamp in UTC (FR-1); UI converts to local time (NFR-5). */
  clockOut: string;
  /** Net worked minutes after break deduction (FR-2, FR-3, NFR-2). */
  totalWorkMinutes: number;
  /** Overtime minutes beyond the shift standard (FR-4, AC-3). 0/null when none. */
  overtimeMinutes: number | null;
  /** Computed work-hours status (§7) driving the summary pill. */
  status: ClockOutStatus;
  /**
   * US-ATT-008 (FR-3, AC-3): true when this clock-out was before the shift end time
   * (and the shift's minimum hours were not met). Drives the "Early by {n} min" badge.
   */
  isEarlyDeparture?: boolean;
  /** US-ATT-008 (AC-3): minutes departed early before shift end; 0/absent when not early. */
  earlyDepartureMinutes?: number;
}

/**
 * Typed error body from the backend for clock-out (US-ATT-002 AC-2).
 * `message` is shown verbatim inline; `code` is the machine-readable discriminator:
 *   - 404 `code: 'no_active_clock_in'` -> "No active clock-in found..." (AC-2) -> reset to clock-in state.
 */
export interface IClockOutErrorResponse {
  message: string;
  code?: 'no_active_clock_in' | string;
}

// ─── US-ATT-003: Attendance Regularization (Forgot Clock-In/Out) ──────────────

/**
 * US-ATT-003 (§7): the kind of correction requested.
 *  - MISSED_CLOCK_IN  : forgot to clock in (requires requestedClockIn).
 *  - MISSED_CLOCK_OUT : clocked in but forgot to clock out (requires requestedClockOut).
 *  - MISSED_BOTH      : no record at all for the day (requires both times).
 */
export type RegularizationType = 'MISSED_CLOCK_IN' | 'MISSED_CLOCK_OUT' | 'MISSED_BOTH';

/** Lifecycle status of a regularization request (§7). Drives the status pill. */
export type RegularizationStatus = 'PENDING' | 'APPROVED' | 'REJECTED' | 'CANCELLED';

/**
 * Request payload to submit a regularization (FR-1, FR-2).
 *
 * Contract (designed against — backend agent building in parallel):
 *   POST /api/v1/attendance/regularizations
 *   body: { date, regularizationType, requestedClockIn?, requestedClockOut?, reason }
 *
 *  - `date` is a calendar date `yyyy-MM-dd` (the day being regularized).
 *  - `requestedClockIn` / `requestedClockOut` are `HH:mm` wall-clock times for that
 *    date (employee-local); the backend combines them with `date` and stores
 *    `timestamptz` (§7). They are conditional on the type — omitted/null when the
 *    type does not require them.
 *  - `reason` is mandatory, min 10 chars (BR-7).
 *
 * The backend stamps tenant_id, employee_id, attendance_log_id, audit fields and
 * the workflow instance server-side (FR-2, FR-3) — the FE never sends them.
 */
export interface ICreateRegularizationRequest {
  /** The calendar date to regularize, `yyyy-MM-dd` (§7). */
  date: string;
  regularizationType: RegularizationType;
  /** `HH:mm` local time; required for MISSED_CLOCK_IN / MISSED_BOTH (§7), else null. */
  requestedClockIn: string | null;
  /** `HH:mm` local time; required for MISSED_CLOCK_OUT / MISSED_BOTH (§7), else null. */
  requestedClockOut: string | null;
  /** Mandatory, min 10 chars (BR-7). */
  reason: string;
}

/**
 * A regularization record returned by the API (§7 attendance_regularization).
 * Returned by both the create endpoint (status 'PENDING') and the list endpoint.
 * Timestamps are UTC `timestamptz` strings (nullable); the UI formats to local.
 */
export interface IRegularization {
  regularizationId: string;
  /**
   * D1 slice 3: `tenantId` was declared here but `RegularizationDto` has never carried it —
   * the tenant is implicit in the JWT + the X-Tenant-Subdomain header, and echoing it into a
   * per-employee response would be a tenant-isolation smell (Critical Rule #1). It was
   * `undefined` at runtime on every row and read only by spec fixtures, so it is gone rather
   * than defaulted to `''`: a blank tenant id in a view model is one refactor away from being
   * used as a cache key.
   */
  employeeId: string;
  /** Linked attendance_log when one already existed (e.g. MISSED_CLOCK_OUT); else null. */
  attendanceLogId: string | null;
  /** The regularized calendar date `yyyy-MM-dd`. */
  date: string;
  regularizationType: RegularizationType;
  /** Requested clock-in (UTC timestamptz); null when not applicable. */
  requestedClockIn: string | null;
  /** Requested clock-out (UTC timestamptz); null when not applicable. */
  requestedClockOut: string | null;
  reason: string;
  status: RegularizationStatus;
  createdAt: string;
}

/**
 * Typed error body for regularization submission (AC-3, AC-4, AC-5).
 * `message` is shown verbatim inline; `code` is an optional machine discriminator
 * (lookback_exceeded / duplicate_pending / payroll_locked) — the UI only displays
 * the message, it does not branch on the code.
 */
export interface IRegularizationErrorResponse {
  message: string;
  code?: string;
}

/** Notion-style status-pill classes per regularization status (§8). */
export const REGULARIZATION_STATUS_CLASSES: Record<RegularizationStatus, string> = {
  PENDING: 'bg-amber-50 text-amber-700 ring-amber-200',
  APPROVED: 'bg-green-50 text-green-700 ring-green-200',
  REJECTED: 'bg-red-50 text-red-700 ring-red-200',
  CANCELLED: 'bg-neutral-100 text-neutral-500 ring-neutral-200',
};

/** Human-readable label for a regularization type (§8). */
export function regularizationTypeLabel(type: RegularizationType): string {
  switch (type) {
    case 'MISSED_CLOCK_IN':
      return 'Missed clock-in';
    case 'MISSED_CLOCK_OUT':
      return 'Missed clock-out';
    case 'MISSED_BOTH':
      return 'Missed both';
  }
}

/** Human-readable label for a regularization status (§8). */
export function regularizationStatusLabel(status: RegularizationStatus): string {
  switch (status) {
    case 'PENDING':
      return 'Pending';
    case 'APPROVED':
      return 'Approved';
    case 'REJECTED':
      return 'Rejected';
    case 'CANCELLED':
      return 'Cancelled';
  }
}

// ─── US-ATT-004: Manager Approves/Rejects Regularization Requests ─────────────

/**
 * US-ATT-004 (§8): a pending regularization request as it appears in the manager's
 * approval queue. Extends {@link IRegularization} with the denormalized fields the
 * queue needs without extra lookups (FR-1, AC-3): the requester's display name and
 * the submission timestamp.
 *
 * Backend endpoint (REAL contract):
 *   GET /api/v1/attendance/regularizations/pending
 *     optional query params: employeeId, fromDate, toDate
 *     -> ApiResponse<PendingRegularizationQueueResult> where
 *        data = { items: IPendingRegularization[], totalCount }
 *        (the service reads `data.items`)
 *
 * The backend scopes the queue to the manager's direct reports (FR-7, BR-1) and the
 * tenant (NFR-3) server-side; the FE never sends a manager/tenant id.
 *
 * NOTE: the queue item is a flat row shape (it does NOT carry tenantId/attendanceLogId
 * like the full {@link IRegularization}); it is the projection the backend returns.
 */
export interface IPendingRegularization {
  regularizationId: string;
  employeeId: string;
  /** Denormalized full name of the requesting employee (AC-3). */
  employeeName: string;
  /** Optional employee photo URL; null/empty -> initials avatar (§8). */
  employeePhoto?: string | null;
  /** The regularized calendar date `yyyy-MM-dd`. */
  date: string;
  regularizationType: RegularizationType;
  /** Requested clock-in (UTC timestamptz); null when not applicable. */
  requestedClockIn?: string | null;
  /** Requested clock-out (UTC timestamptz); null when not applicable. */
  requestedClockOut?: string | null;
  reason: string;
  /** When the request was submitted (AC-3 "submission date"). */
  submittedOn: string;
}

/**
 * US-ATT-004 (REAL contract): the `data` payload of the pending-queue endpoint.
 *   GET /api/v1/attendance/regularizations/pending
 *   -> ApiResponse<PendingRegularizationQueueResult>
 */
export interface IPendingRegularizationQueueResult {
  items: IPendingRegularization[];
  totalCount: number;
}

/** Optional filters for the pending-queue endpoint (query params). */
export interface IPendingRegularizationQuery {
  employeeId?: string;
  fromDate?: string;
  toDate?: string;
}

/** The action a manager takes on a regularization request (§7 `action`). */
export type RegularizationAction = 'APPROVE' | 'REJECT';

/**
 * US-ATT-004 (REAL contract): body for a single APPROVE action.
 *   POST /api/v1/attendance/regularizations/{id}/approve   (id in the PATH)
 *   body { comment? }
 *
 * `comment` is optional for APPROVE (BR-2).
 */
export interface IApproveRegularizationRequest {
  /** Optional approval note (BR-2). */
  comment?: string;
}

/**
 * US-ATT-004 (REAL contract): body for a single REJECT action.
 *   POST /api/v1/attendance/regularizations/{id}/reject    (id in the PATH)
 *   body { reason }   (the field is `reason`, NOT `comment`; min 10 chars, BR-1/FR-3)
 *
 * The component enforces the min-10-chars rule before calling.
 */
export interface IRejectRegularizationRequest {
  /** Required rejection reason, min 10 chars (BR-1). NOTE: `reason`, not `comment`. */
  reason: string;
}

/**
 * US-ATT-004 (BR-7) REAL contract: body for the bulk-approve action.
 *   POST /api/v1/attendance/regularizations/bulk-approve
 *   body { regularizationIds, comment? }   (no `action` field)
 */
export interface IBulkApproveRequest {
  regularizationIds: string[];
  /** Optional shared comment applied to every approved request (BR-2). */
  comment?: string;
}

/**
 * US-ATT-004 (REAL contract): result of an approve/reject action — the backend's
 * `RegularizationDecisionDto`. On REJECT the log/computation fields are null.
 */
export interface IRegularizationDecisionDto {
  regularizationId: string;
  /**
   * D1 slice 3: widened to include 'PENDING'. On a multi-level approval workflow the backend
   * records the approver's decision and returns `Status = PENDING` with `Action = APPROVED` —
   * the step advanced, the regularization is NOT yet approved. The old two-value union made
   * that state unrepresentable, which is why the UI still reports it as final (see the
   * OUT-OF-LANE note in the migration report).
   */
  status: 'PENDING' | 'APPROVED' | 'REJECTED';
  action: string;
  approvalLevel: number;
  /** Created/linked attendance log on APPROVE; null on REJECT. */
  attendanceLogId: string | null;
  /** Computed net worked minutes on APPROVE; null on REJECT. */
  totalWorkMinutes: number | null;
  /** Computed overtime minutes on APPROVE; null on REJECT. */
  overtimeMinutes: number | null;
  /** Computed work-hours status on APPROVE; null on REJECT. */
  attendanceStatus: string | null;
  /** Optional comment echoed back. */
  comment?: string | null;
  /** When the decision was actioned (UTC timestamptz). */
  actionedAt: string;
}

/**
 * Per-item result of a bulk approve (BR-7) REAL contract. The backend processes each
 * id independently so a partial failure (AC-5/BR-5) does not roll back the rest; the
 * FE removes rows where `succeeded === true` and surfaces `error` verbatim otherwise.
 */
export interface IBulkApproveItemResult {
  regularizationId: string;
  /** Per-item success flag (NOTE: `succeeded`, not `success`). */
  succeeded: boolean;
  /** The decision when the item succeeded. */
  decision?: IRegularizationDecisionDto;
  /** Server message for a failed item, shown verbatim (AC-5 / BR-5). */
  error?: string;
  /** Optional machine error code (e.g. payroll_period_locked). */
  errorCode?: string;
}

/** Result returned by the bulk-approve endpoint (BR-7) REAL contract. */
export interface IBulkApproveResult {
  totalRequested: number;
  succeededCount: number;
  failedCount: number;
  /** Per-id results (NOTE: `items`, not `results`). */
  items: IBulkApproveItemResult[];
}

/**
 * Typed error body for an approve/reject/bulk action (AC-5 authorization denial,
 * BR-5 payroll-locked period). `message` is shown verbatim; `code` is an optional
 * machine discriminator the UI does not branch on.
 *   - 403 `code: 'not_authorized'` -> "You are not authorized to approve requests
 *     for this employee." (AC-5)
 *   - 409/400 `code: 'payroll_locked'` -> locked-period block (BR-5)
 */
export interface IRegularizationActionErrorResponse {
  message: string;
  code?: 'not_authorized' | 'payroll_locked' | 'already_actioned' | string;
}

/** Minimal shape of the backend `ApiResponse<T>` envelope the service unwraps. */
export interface IAttendanceApiEnvelope<T> {
  data: T;
  success?: boolean;
  message?: string;
}

// ─── US-ATT-005: Shift Management and Assignment ──────────────────────────────

/**
 * US-ATT-005 (FR-1, §7): the three supported shift types.
 *  - SINGLE   : fixed start/end times (start != end, BR-7).
 *  - ROTATING : cyclic pattern across several sub-shifts (FR-7, AC-5); start/end on
 *               the parent are optional, the rotation steps carry the schedule.
 *  - FLEXIBLE : no fixed start/end; only `minimumHours` is enforced (BR-8).
 */
export type ShiftType = 'SINGLE' | 'ROTATING' | 'FLEXIBLE';

export const SHIFT_TYPE_OPTIONS: ShiftType[] = ['SINGLE', 'ROTATING', 'FLEXIBLE'];

/**
 * One step of a rotating-shift cycle (FR-7, AC-5). The cycle repeats indefinitely
 * from `referenceStartDate`; each step points at an existing shift definition that
 * applies for `durationDays` consecutive days, in `order` sequence.
 */
export interface IRotationStep {
  /** 1-based position of this step within the cycle. */
  order: number;
  /** The shift definition applied during this step (an existing SINGLE/FLEXIBLE shift id). */
  shiftId: string;
  /** How many consecutive days this step lasts. */
  durationDays: number;
}

/**
 * US-ATT-005 (FR-7): the rotation pattern attached to a ROTATING shift. The backend
 * uses `cycleLengthDays` + `referenceStartDate` + ordered `steps` to compute the
 * applicable shift for any given date (AC-5).
 */
export interface IRotation {
  /** Total length of the repeating cycle in days (usually sum of step durations). */
  cycleLengthDays: number;
  /** Anchor date the cycle counts from, `yyyy-MM-dd`. */
  referenceStartDate: string;
  steps: IRotationStep[];
}

/**
 * US-ATT-005 shift definition returned by the API (§7 `shift` table).
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   GET    /api/v1/attendance/shifts                 -> ShiftDto[]
 *   POST   /api/v1/attendance/shifts                 -> ShiftDto
 *   PUT    /api/v1/attendance/shifts/{id}            -> ShiftDto
 *   DELETE /api/v1/attendance/shifts/{id}            -> 204 (409 shift_in_use when assigned)
 *   POST   /api/v1/attendance/shifts/{id}/clone      -> ShiftDto
 *   POST   /api/v1/attendance/shifts/{id}/assign     -> { assignedCount, employeeShiftIds }
 *   GET    /api/v1/attendance/employees/{id}/shift?date=  -> ResolvedShiftDto
 *
 * Times are `HH:mm` 24h strings (null for FLEXIBLE). `workingDays` is an array of
 * ISO day numbers (1=Mon .. 7=Sun, BR-6).
 */
export interface IShift {
  id: string;
  name: string;
  type: ShiftType;
  /** `HH:mm`; null for FLEXIBLE (BR-8). */
  startTime: string | null;
  /** `HH:mm`; null for FLEXIBLE (BR-8). Night shifts allow end < start (§10). */
  endTime: string | null;
  breakDurationMinutes: number;
  gracePeriodMinutes: number;
  /** Required total hours; set for FLEXIBLE (BR-8), else null. */
  minimumHours: number | null;
  /** ISO day numbers 1=Mon..7=Sun (BR-6). */
  workingDays: number[];
  /** The tenant's default shift (BR-1, FR-5). */
  isDefault: boolean;
  isActive: boolean;
  /** Count of employees currently assigned (drives the AC-4 delete guard). */
  assignedEmployeeCount: number;
  /** Present for ROTATING shifts (FR-7). */
  rotation?: IRotation;
}

/**
 * US-ATT-005 create/update payload (FR-2). Mirrors {@link IShift} minus the
 * server-owned fields (id, isDefault, isActive, assignedEmployeeCount). The backend
 * stamps tenant_id + audit fields server-side (NFR-3). Times are omitted for FLEXIBLE.
 */
export interface IShiftRequest {
  name: string;
  type: ShiftType;
  /** Omitted/undefined for FLEXIBLE (BR-8). */
  startTime?: string;
  endTime?: string;
  breakDurationMinutes: number;
  gracePeriodMinutes: number;
  /** Required for FLEXIBLE (BR-8); omitted otherwise. */
  minimumHours?: number;
  workingDays: number[];
  /** Present only for ROTATING (FR-7). */
  rotation?: IRotation;
}

/**
 * US-ATT-005 (AC-2): payload to bulk-assign a shift to employees with an effective
 * date. `effectiveFrom` is `yyyy-MM-dd`; the backend closes any current assignment
 * and opens a new effective-dated one without overlap (AC-3, BR-2/BR-3).
 *   POST /api/v1/attendance/shifts/{id}/assign  body { employeeIds, effectiveFrom }
 */
export interface IShiftAssignmentRequest {
  employeeIds: string[];
  /** `yyyy-MM-dd` effective date for the new assignment(s) (BR-3). */
  effectiveFrom: string;
}

/**
 * US-ATT-005 (AC-2) result of a bulk assignment. The FE shows `assignedCount`
 * verbatim in the success toast.
 */
export interface IAssignmentResult {
  assignedCount: number;
  employeeShiftIds: string[];
}

/**
 * US-ATT-005 (FR-7, AC-5): the shift resolved as applicable to a specific employee on
 * a specific date — a {@link IShift} plus the effective-dating window it falls in.
 *   GET /api/v1/attendance/employees/{employeeId}/shift?date=yyyy-MM-dd
 */
export interface IResolvedShift extends IShift {
  /** Start of the assignment window covering `resolvedForDate`, `yyyy-MM-dd`. */
  effectiveFrom: string;
  /** End of the window, `yyyy-MM-dd`; null when this is the current assignment. */
  effectiveTo: string | null;
  /** The date the resolution was requested for, `yyyy-MM-dd`. */
  resolvedForDate: string;
}

/**
 * US-ATT-005 (AC-4): typed 409 body when deleting a shift that has active
 * assignments. `message` is shown verbatim; `code` is the discriminator.
 *   409 { message: "This shift is assigned to {N} employees...", code: "shift_in_use" }
 */
export interface IShiftInUseErrorResponse {
  message: string;
  code?: 'shift_in_use' | string;
}

// ─── US-ATT-006: Overtime Tracking and Approval ──────────────────────────────

/**
 * US-ATT-006 (§7 `status`): the lifecycle status of an overtime record.
 *  - PENDING    : awaiting manager approval (amber pill, §8).
 *  - APPROVED   : approved (possibly with adjusted minutes) — payroll-ready (green).
 *  - REJECTED   : rejected by the manager (red).
 *  - UNAPPROVED : auto-detected overtime worked without the required pre-approval
 *                 (BR-6) — excluded from payroll until HR reviews it (gray).
 */
export type OvertimeStatus = 'PENDING' | 'APPROVED' | 'REJECTED' | 'UNAPPROVED';

/**
 * US-ATT-006 (§7 `type`, FR-2): how the overtime record originated.
 *  - AUTO_DETECTED : created automatically on clock-out (AC-1).
 *  - PRE_APPROVED  : raised ahead of time via the pre-approval form (AC-2, FR-4).
 */
export type OvertimeType = 'AUTO_DETECTED' | 'PRE_APPROVED';

/**
 * US-ATT-006 overtime record returned by the API (§7 `overtime_record`).
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   POST /api/v1/attendance/overtime/pre-approval  body { date, expectedHours, reason } -> OvertimeDto
 *   GET  /api/v1/attendance/overtime/my            -> OvertimeDto[]
 *   GET  /api/v1/attendance/overtime/pending       -> { items: OvertimeQueueItemDto[], totalCount }
 *   POST /api/v1/attendance/overtime/{id}/approve  body { approvedMinutes?, comment? } -> OvertimeDecisionDto
 *   POST /api/v1/attendance/overtime/{id}/reject   body { reason } (>=10) -> OvertimeDecisionDto
 *   GET  /api/v1/attendance/overtime/report?month=yyyy-MM -> { month, items: OvertimeReportRowDto[], totals }
 *
 * All envelopes are ApiResponse<T> = { success, data, message }; the service unwraps `.data`.
 * `multiplier` is the applied rate (1.50/2.00/2.50, BR-3). `approvedMinutes` is set on
 * approval and may differ from `overtimeMinutes` after a manager adjustment (FR-6).
 */
export interface IOvertime {
  id: string;
  employeeId: string;
  /** Linked attendance log when auto-detected (AC-1); null for a pre-approval. */
  attendanceLogId?: string | null;
  /** The overtime calendar date `yyyy-MM-dd`. */
  date: string;
  /** Actual overtime duration in minutes (FR-2). For PRE_APPROVED, the expected amount. */
  overtimeMinutes: number;
  /** Set on approval (FR-6); null while PENDING/REJECTED/UNAPPROVED. */
  approvedMinutes: number | null;
  /** Applied multiplier rate, e.g. 1.50, 2.00 (BR-3). */
  multiplier: number;
  type: OvertimeType;
  status: OvertimeStatus;
  /** Employee or system reason (§7). */
  reason: string;
  /** Manager note set on approve/reject; null otherwise. */
  managerComment: string | null;
  /** Record creation timestamp (UTC); the UI formats to local. */
  createdAt: string;
}

/**
 * US-ATT-006 (AC-2, FR-4): payload to submit an overtime pre-approval request.
 *   POST /api/v1/attendance/overtime/pre-approval  body { date, expectedHours, reason }
 *
 *  - `date` is `yyyy-MM-dd` (the planned overtime day).
 *  - `expectedHours` is the expected overtime in hours (decimal allowed).
 *  - `reason` is mandatory (min 10 chars, enforced by the form).
 * The backend stamps tenant_id, employee_id, type=PRE_APPROVED and audit fields server-side.
 */
export interface IOvertimePreApprovalRequest {
  date: string;
  expectedHours: number;
  reason: string;
}

/**
 * US-ATT-006 (§8, AC-3): an overtime record as it appears in the manager's approval
 * queue — {@link IOvertime} plus the denormalized requester fields the queue needs.
 *   GET /api/v1/attendance/overtime/pending
 *     -> ApiResponse<{ items: IOvertimeQueueItem[], totalCount }>  (service reads data.items)
 * The backend scopes to the manager's team + tenant server-side (BR-8, NFR-2).
 */
export interface IOvertimeQueueItem extends IOvertime {
  /** Denormalized full name of the requesting employee (AC-3). */
  employeeName: string;
  /** Optional employee photo URL; null/empty -> initials avatar (§8). */
  employeePhoto?: string | null;
  /** When the overtime was submitted/detected (AC-3). */
  submittedOn: string;
}

/** US-ATT-006: the `data` payload of the pending-overtime-queue endpoint. */
export interface IOvertimeQueueResult {
  items: IOvertimeQueueItem[];
  totalCount: number;
}

/**
 * US-ATT-006 (FR-6, AC-4): body for an APPROVE action.
 *   POST /api/v1/attendance/overtime/{id}/approve  body { approvedMinutes?, comment? }
 * `approvedMinutes` lets the manager adjust the awarded minutes down/up (FR-6); omitted
 * approves the full requested amount. `comment` is optional.
 */
export interface IOvertimeApproveRequest {
  approvedMinutes?: number;
  comment?: string;
}

/**
 * US-ATT-006: body for a REJECT action.
 *   POST /api/v1/attendance/overtime/{id}/reject  body { reason }  (min 10 chars)
 */
export interface IOvertimeRejectRequest {
  reason: string;
}

/**
 * US-ATT-006 (AC-4): result of an approve/reject action — the backend's
 * `OvertimeDecisionDto`. On REJECT `approvedMinutes` is null.
 */
export interface IOvertimeDecision {
  id: string;
  status: OvertimeStatus;
  /** Awarded minutes on APPROVE (may differ from requested, FR-6); null on REJECT. */
  approvedMinutes: number | null;
  multiplier: number;
  /** Optional manager comment echoed back. */
  managerComment?: string | null;
  /** When the decision was actioned (UTC timestamptz). */
  actionedAt: string;
}

/**
 * US-ATT-006 (AC-5): one row of the monthly overtime report — aggregated minutes by
 * employee for the selected month.
 *   GET /api/v1/attendance/overtime/report?month=yyyy-MM
 *     -> ApiResponse<{ month, items: IOvertimeReportRow[], totals }>
 */
export interface IOvertimeReportRow {
  employeeId: string;
  employeeName: string;
  approvedMinutes: number;
  pendingMinutes: number;
  rejectedMinutes: number;
  recordCount: number;
}

/** US-ATT-006 (AC-5): the full monthly overtime report payload. */
export interface IOvertimeReportResult {
  /** The reported month, `yyyy-MM`. */
  month: string;
  items: IOvertimeReportRow[];
  /** Aggregate totals across all employees for the month. */
  totals: {
    approvedMinutes: number;
    pendingMinutes: number;
    rejectedMinutes: number;
    recordCount: number;
  };
}

/**
 * Typed error body for an overtime action (AC-4, BR-8). `message` is shown verbatim;
 * `code` is an optional machine discriminator the UI does not branch on.
 *   - 403 `code: 'self_approval'`   -> manager cannot approve their own overtime (BR-8)
 *   - 403 `code: 'not_team_member'` -> not the employee's approver
 *   - 409 `code: 'already_actioned'`-> the record was already decided
 */
export interface IOvertimeActionErrorResponse {
  message: string;
  code?: 'self_approval' | 'not_team_member' | 'already_actioned' | string;
}

/** Notion-style status-pill classes per overtime status (§8). */
export const OVERTIME_STATUS_CLASSES: Record<OvertimeStatus, string> = {
  PENDING: 'bg-amber-50 text-amber-700 ring-amber-200',
  APPROVED: 'bg-green-50 text-green-700 ring-green-200',
  REJECTED: 'bg-red-50 text-red-700 ring-red-200',
  UNAPPROVED: 'bg-neutral-100 text-neutral-500 ring-neutral-200',
};

/** Human-readable label for an overtime status (§8). */
export function overtimeStatusLabel(status: OvertimeStatus): string {
  switch (status) {
    case 'PENDING':
      return 'Pending';
    case 'APPROVED':
      return 'Approved';
    case 'REJECTED':
      return 'Rejected';
    case 'UNAPPROVED':
      return 'Unapproved';
  }
}

/** Human-readable label for an overtime type (§8). */
export function overtimeTypeLabel(type: OvertimeType): string {
  switch (type) {
    case 'AUTO_DETECTED':
      return 'Auto-detected';
    case 'PRE_APPROVED':
      return 'Pre-approved';
  }
}

/** Format a multiplier rate for display, e.g. 1.5 -> "1.5x" (BR-3, §8). */
export function formatMultiplier(multiplier: number): string {
  if (multiplier == null || Number.isNaN(multiplier)) {
    return '—';
  }
  // Trim a trailing ".0" so 2.0 -> "2x" but 1.5 -> "1.5x".
  const trimmed = Number.isInteger(multiplier)
    ? multiplier.toString()
    : multiplier.toString().replace(/0+$/, '').replace(/\.$/, '');
  return `${trimmed}x`;
}

/**
 * Default tenant weekly overtime cap in minutes (BR-5 default 20h) used to derive the
 * weekly-progress bar (§8). The exact cap is tenant-configurable server-side; the FE
 * uses this as a sensible display default until a policy endpoint exposes it.
 */
export const WEEKLY_OVERTIME_CAP_MINUTES = 20 * 60;

/**
 * Pure helper: total a list of overtime records' minutes for the current ISO week
 * (Mon–Sun) containing `reference`, used to feed the weekly-progress bar (§8). Only
 * APPROVED + PENDING minutes count toward the cap; REJECTED/UNAPPROVED are excluded.
 * Uses `approvedMinutes` when set (post-adjustment), else `overtimeMinutes`.
 */
export function weeklyOvertimeMinutes(
  records: readonly IOvertime[],
  reference: Date = new Date(),
): number {
  const ref = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate());
  // ISO week: Monday is the first day. getDay() -> 0=Sun..6=Sat.
  const day = ref.getDay();
  const mondayOffset = day === 0 ? -6 : 1 - day;
  const monday = new Date(ref);
  monday.setDate(ref.getDate() + mondayOffset);
  const sunday = new Date(monday);
  sunday.setDate(monday.getDate() + 7); // exclusive upper bound

  return records.reduce((sum, r) => {
    if (r.status === 'REJECTED' || r.status === 'UNAPPROVED') {
      return sum;
    }
    const d = new Date(`${r.date}T00:00:00`);
    if (Number.isNaN(d.getTime()) || d < monday || d >= sunday) {
      return sum;
    }
    const minutes = r.approvedMinutes != null ? r.approvedMinutes : r.overtimeMinutes;
    return sum + (minutes ?? 0);
  }, 0);
}

// ─── US-ATT-007: Monthly Attendance Summary per Employee ─────────────────────

/**
 * US-ATT-007 (§7 `attendance_monthly_summary`): one employee's aggregated attendance
 * for a month. Durations are in minutes (work/overtime); day counts are decimals to
 * support half-days (BR-5). Returned in {@link IMonthlySummaryResult.rows}.
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   GET /api/v1/attendance/summary/monthly?month=yyyy-MM&departmentId=&locationId=&shiftId=&status=
 *     -> ApiResponse<MonthlySummaryResult>
 */
export interface IEmployeeMonthlySummary {
  employeeId: string;
  employeeName: string;
  /** Optional payroll/display employee number (§8). */
  employeeNumber?: string | null;
  /** Optional denormalized department name for the row (AC-5, §8). */
  departmentName?: string | null;
  /** Days present (BR-1); decimal to allow 0.5 half-days (BR-5). */
  presentDays: number;
  /** Scheduled working days with no record + no leave (BR-2). */
  absentDays: number;
  /** Count of late arrivals in the month (US-ATT-008). */
  lateCount: number;
  /** Count of early departures in the month (US-ATT-008). */
  earlyDepartureCount: number;
  /** Total net worked minutes across the month (FR-3, NFR-5). */
  workMinutes: number;
  /** Total approved overtime minutes (US-ATT-006). */
  overtimeMinutes: number;
  /** Approved leave days, reconciled from the Leave module (BR-6). */
  leaveDays: number;
  /** Public holidays in the month (BR-4). */
  holidays: number;
  /** Weekly-off days in the month (BR-4). */
  weeklyOffs: number;
  /** Loss-of-Pay days — absent days not covered by leave (BR-3); feeds payroll. */
  lopDays: number;
  /** When this employee's summary row was last computed (UTC timestamptz). */
  generatedAt: string;
}

/**
 * US-ATT-007 (§8): the summary banner aggregates shown above the table.
 */
export interface IMonthlySummaryBanner {
  totalEmployees: number;
  /**
   * Average attendance percentage across the listed employees (0–100), or null
   * when the server did not supply it. Null is NOT 0: on a percentage, 0 is the
   * worst value in the range, so defaulting an absent figure to 0 would render a
   * headline claim of total absenteeism. Unknown renders as an em dash.
   */
  averageAttendancePercent: number | null;
  /** Total Loss-of-Pay days across the listed employees (BR-3). */
  totalLopDays: number;
}

/**
 * US-ATT-007 (AC-1): the full monthly-summary payload (the `data` of the envelope).
 *   GET /api/v1/attendance/summary/monthly -> ApiResponse<MonthlySummaryResult>
 * `generatedAt` is null when the summary has not yet been computed for the month
 * (AC-3) — the FE then offers on-demand generation.
 */
export interface IMonthlySummaryResult {
  /** The reported month, `yyyy-MM`. */
  yearMonth: string;
  rows: IEmployeeMonthlySummary[];
  banner: IMonthlySummaryBanner;
  /** When the month's summary was generated; null when not yet generated (AC-3). */
  generatedAt: string | null;
}

/** Optional filters for the monthly-summary endpoint (query params, AC-5, FR-5). */
export interface IMonthlySummaryQuery {
  /** `yyyy-MM`. */
  month: string;
  departmentId?: string;
  locationId?: string;
  shiftId?: string;
  /** Employee status filter (e.g. ACTIVE), passed through to the backend (FR-5). */
  status?: string;
}

/**
 * US-ATT-007 (AC-2, §8): the status of a single day in an employee's monthly breakdown.
 */
export type DailyBreakdownStatus =
  | 'PRESENT'
  | 'ABSENT'
  | 'LEAVE'
  | 'HOLIDAY'
  | 'WEEKLY_OFF'
  | 'HALF_DAY';

/**
 * US-ATT-007 (AC-2): one day of an employee's monthly attendance breakdown — the
 * drill-down row behind a summary row.
 *   GET /api/v1/attendance/summary/monthly/{employeeId}?month=yyyy-MM
 *     -> ApiResponse<EmployeeDailyBreakdownResult>
 */
export interface IDailyBreakdown {
  /** The calendar date, `yyyy-MM-dd`. */
  date: string;
  status: DailyBreakdownStatus;
  /** Clock-in (UTC timestamptz); null on a non-present day. */
  clockIn?: string | null;
  /** Clock-out (UTC timestamptz); null while open or on a non-present day. */
  clockOut?: string | null;
  /** Net worked minutes for the day; null on a non-present day. */
  workMinutes?: number | null;
  /** True when the day's attendance was created via an approved regularization (BR-7). */
  isRegularized: boolean;
  /** True when the clock-in was flagged late (US-ATT-008). */
  isLate: boolean;
  /** True when the clock-out was flagged an early departure (US-ATT-008). */
  isEarlyDeparture: boolean;
}

/**
 * US-ATT-007 (AC-2): the daily-breakdown payload for one employee/month (envelope `data`).
 */
export interface IEmployeeDailyBreakdownResult {
  employeeId: string;
  employeeName: string;
  /** `yyyy-MM`. */
  yearMonth: string;
  days: IDailyBreakdown[];
}

/**
 * US-ATT-007 (AC-3): on-demand summary-generation status returned by the generate
 * endpoint (and re-fetched while polling until COMPLETED).
 *   POST /api/v1/attendance/summary/monthly/generate?month=yyyy-MM
 *     -> ApiResponse<SummaryGenerationStatusDto>
 */
export interface ISummaryGenerationStatus {
  /** `yyyy-MM`. */
  yearMonth: string;
  status: 'PENDING' | 'RUNNING' | 'COMPLETED';
  /** Set once the generation has finished (UTC timestamptz); null while in progress. */
  generatedAt: string | null;
}

/** US-ATT-007 (AC-4): export formats supported by the summary export endpoint (FR-6). */
export type SummaryExportFormat = 'csv' | 'xlsx' | 'pdf';

/** Notion-style cell classes per daily-breakdown status (AC-2, §8). */
export const DAILY_STATUS_CLASSES: Record<DailyBreakdownStatus, string> = {
  PRESENT: 'bg-green-50 text-green-700 ring-green-200',
  ABSENT: 'bg-red-50 text-red-700 ring-red-200',
  LEAVE: 'bg-blue-50 text-blue-700 ring-blue-200',
  HOLIDAY: 'bg-purple-50 text-purple-700 ring-purple-200',
  WEEKLY_OFF: 'bg-neutral-100 text-neutral-500 ring-neutral-200',
  HALF_DAY: 'bg-amber-50 text-amber-700 ring-amber-200',
};

/** Human-readable label for a daily-breakdown status (AC-2, §8). */
export function dailyStatusLabel(status: DailyBreakdownStatus): string {
  switch (status) {
    case 'PRESENT':
      return 'Present';
    case 'ABSENT':
      return 'Absent';
    case 'LEAVE':
      return 'Leave';
    case 'HOLIDAY':
      return 'Holiday';
    case 'WEEKLY_OFF':
      return 'Weekly off';
    case 'HALF_DAY':
      return 'Half day';
  }
}

/**
 * US-ATT-007 (§8): derive the Notion-style cell color class for a summary count cell,
 * given a value and a "high" threshold. Absent cells go red above the threshold; late
 * cells amber; a zero-absent / zero-late cell renders neutral (no alarm color).
 */
export function summaryCellClass(
  value: number,
  threshold: number,
  tone: 'absent' | 'late',
): string {
  if (value <= 0) {
    return 'text-neutral-400';
  }
  if (value >= threshold) {
    return tone === 'absent'
      ? 'text-red-700 font-semibold'
      : 'text-amber-700 font-semibold';
  }
  return 'text-neutral-700';
}

/**
 * US-ATT-007 (§8): the per-employee attendance percentage used for the "full attendance"
 * green highlight and the mini bar. present / (present + absent) * 100; 0 when neither.
 */
export function attendancePercent(row: IEmployeeMonthlySummary): number {
  const scheduled = row.presentDays + row.absentDays;
  if (scheduled <= 0) {
    return 0;
  }
  return Math.round((row.presentDays / scheduled) * 100);
}

/** ISO day-of-week labels indexed 1=Mon .. 7=Sun (BR-6, §8). */
export const WEEKDAY_LABELS: Record<number, string> = {
  1: 'Mon',
  2: 'Tue',
  3: 'Wed',
  4: 'Thu',
  5: 'Fri',
  6: 'Sat',
  7: 'Sun',
};

/** Ordered ISO weekday list (Mon..Sun) for rendering working-day pickers (§8). */
export const ISO_WEEKDAYS: number[] = [1, 2, 3, 4, 5, 6, 7];

/** Human-readable label for a shift type (§8). */
export function shiftTypeLabel(type: ShiftType): string {
  switch (type) {
    case 'SINGLE':
      return 'Single';
    case 'ROTATING':
      return 'Rotating';
    case 'FLEXIBLE':
      return 'Flexible';
  }
}

/** Whether the shift type uses fixed start/end times (SINGLE or ROTATING parent). */
export function shiftTypeUsesTimes(type: ShiftType): boolean {
  return type === 'SINGLE' || type === 'ROTATING';
}

/**
 * Format a shift's working-days array as a compact label, e.g. [1,2,3,4,5] -> "Mon–Fri",
 * arbitrary sets -> "Mon, Wed, Fri", empty -> "—" (§8).
 */
export function formatWorkingDays(days: number[]): string {
  if (!days || days.length === 0) {
    return '—';
  }
  const sorted = [...new Set(days)].sort((a, b) => a - b);
  // Contiguous run detection for a tidy range label.
  const isContiguous = sorted.every((d, i) => i === 0 || d === sorted[i - 1] + 1);
  if (isContiguous && sorted.length >= 3) {
    return `${WEEKDAY_LABELS[sorted[0]]}–${WEEKDAY_LABELS[sorted[sorted.length - 1]]}`;
  }
  return sorted.map((d) => WEEKDAY_LABELS[d]).join(', ');
}

/** Format a shift's time band, e.g. "09:00 – 17:00"; FLEXIBLE -> "Flexible" (§8). */
export function formatShiftTimes(shift: Pick<IShift, 'type' | 'startTime' | 'endTime'>): string {
  if (shift.type === 'FLEXIBLE') {
    return 'Flexible';
  }
  if (!shift.startTime || !shift.endTime) {
    return '—';
  }
  return `${shift.startTime} – ${shift.endTime}`;
}

/** Format a requested-time range for the queue "Requested Times" column (§8). */
export function formatRequestedTimes(
  reg: { requestedClockIn?: string | null; requestedClockOut?: string | null },
): string {
  const fmt = (iso: string | null | undefined): string | null => {
    if (!iso) {
      return null;
    }
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return null;
    }
    const hh = d.getHours().toString().padStart(2, '0');
    const mm = d.getMinutes().toString().padStart(2, '0');
    return `${hh}:${mm}`;
  };
  const inT = fmt(reg.requestedClockIn);
  const outT = fmt(reg.requestedClockOut);
  if (inT && outT) {
    return `${inT} – ${outT}`;
  }
  if (inT) {
    return `In ${inT}`;
  }
  if (outT) {
    return `Out ${outT}`;
  }
  return '—';
}

/** Whether the type requires a clock-in time (FR-1, §7). */
export function typeRequiresClockIn(type: RegularizationType): boolean {
  return type === 'MISSED_CLOCK_IN' || type === 'MISSED_BOTH';
}

/** Whether the type requires a clock-out time (FR-1, §7). */
export function typeRequiresClockOut(type: RegularizationType): boolean {
  return type === 'MISSED_CLOCK_OUT' || type === 'MISSED_BOTH';
}

/**
 * Today's date as a `yyyy-MM-dd` string in the browser's local timezone (BR-4).
 * Used as the max for the date picker and to default/pre-populate the form.
 */
export function todayLocalIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  const d = now.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Pure helper: format a whole-minute duration as "Hh Mm" (e.g. 465 -> "7h 45m") (§8, AC-1).
 * Sub-hour durations render as "Mm" (e.g. 45 -> "45m"); zero -> "0m". Clamps negatives.
 */
export function formatWorkMinutes(totalMinutes: number): string {
  const safe = Math.max(0, Math.floor(totalMinutes));
  const hours = Math.floor(safe / 60);
  const minutes = safe % 60;
  if (hours === 0) {
    return `${minutes}m`;
  }
  return `${hours}h ${minutes}m`;
}

/**
 * Pure helper: format elapsed milliseconds as a live work timer "HH:MM:SS" (§8).
 * Clamps negatives to zero so a clock-skewed start never renders a negative timer.
 */
export function formatElapsed(elapsedMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(elapsedMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
}

/**
 * Pure helper: build a static OpenStreetMap embed URL for a tiny location preview (§8).
 * Uses the free OSM `export/embed` endpoint — no API key, no heavy maps dependency.
 * A small bounding box is derived around the point so the marker sits centered.
 */
export function buildStaticMapUrl(latitude: number, longitude: number): string {
  const delta = 0.005;
  const left = (longitude - delta).toFixed(6);
  const right = (longitude + delta).toFixed(6);
  const bottom = (latitude - delta).toFixed(6);
  const top = (latitude + delta).toFixed(6);
  const bbox = `${left},${bottom},${right},${top}`;
  const marker = `${latitude.toFixed(6)},${longitude.toFixed(6)}`;
  return (
    'https://www.openstreetmap.org/export/embed.html' +
    `?bbox=${encodeURIComponent(bbox)}&layer=mapnik&marker=${encodeURIComponent(marker)}`
  );
}

// ─── US-ATT-008: Late Arrival & Early Departure Tracking ─────────────────────

/**
 * US-ATT-008 (§7 `late_policy`, FR-4): the tenant-level late-arrival policy. HR edits
 * this via the policy-config form (AC-4); the backend applies it to deductions (BR-4)
 * and the chronic-lateness HR escalation (FR-7).
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   GET /api/v1/attendance/late-policy            -> ApiResponse<LatePolicyDto>
 *   PUT /api/v1/attendance/late-policy  body Dto  -> ApiResponse<LatePolicyDto>
 *
 * Envelope is ApiResponse<T> = { success, data, message }; the service unwraps `.data`.
 */
export interface ILatePolicy {
  /** Number of lates in the period that triggers the deduction (FR-4, BR-4). */
  thresholdCount: number;
  /** Day(s) deducted once the threshold is hit, e.g. 0.5 (BR-4). */
  deductionDays: number;
  /** The accumulation window the threshold/deduction applies over (§7). */
  period: 'MONTHLY' | 'QUARTERLY';
  /** FR-5: send the employee an in-app notification on each late arrival. */
  notificationOnLate: boolean;
  /** FR-7: lates/month above this escalate to HR (chronic lateness). */
  chronicThreshold: number;
  /** Whether the policy is currently enforced. */
  isActive: boolean;
}

/** US-ATT-008 (FR-4): the accumulation periods the policy supports. */
export const LATE_POLICY_PERIODS: ILatePolicy['period'][] = ['MONTHLY', 'QUARTERLY'];

/** Human-readable label for a late-policy period (§8). */
export function latePolicyPeriodLabel(period: ILatePolicy['period']): string {
  return period === 'QUARTERLY' ? 'Quarterly' : 'Monthly';
}

/**
 * US-ATT-008 (AC-5, FR-6): one employee row of the late/early-departure report —
 * aggregated late and early-departure counts/minutes for the selected date range.
 *
 *   GET /api/v1/attendance/late-early/report?from=&to=&departmentId=&employeeId=&scope=team|all
 *     -> ApiResponse<{ from, to, rows: LateEarlyRowDto[] }>
 */
export interface ILateEarlyRow {
  employeeId: string;
  employeeName: string;
  /** Denormalized department name for the row (FR-6 department filter context). */
  departmentName?: string | null;
  /** Count of late arrivals in the range (AC-5). */
  lateCount: number;
  /** Total minutes late across the range (§8). */
  totalLateMinutes: number;
  /** Count of early departures in the range (AC-5). */
  earlyDepartureCount: number;
  /** Total early-departure minutes across the range (§8). */
  totalEarlyMinutes: number;
  /** FR-7: true when this employee crossed the chronic-lateness threshold -> amber row. */
  isChronic: boolean;
}

/**
 * US-ATT-008 (AC-5, FR-6): the full late/early report payload (envelope `data`).
 *   GET /api/v1/attendance/late-early/report -> ApiResponse<LateEarlyReportResult>
 */
export interface ILateEarlyReportResult {
  /** Report range start, `yyyy-MM-dd`. */
  from: string;
  /** Report range end, `yyyy-MM-dd`. */
  to: string;
  rows: ILateEarlyRow[];
}

/**
 * US-ATT-008 (FR-6): the report scope. Managers see their team; HR can switch to all.
 *  - 'team' : the authenticated manager's direct reports (default for managers).
 *  - 'all'  : every employee in the tenant (HR-only, backend-enforced).
 */
export type LateEarlyScope = 'team' | 'all';

/** US-ATT-008 (FR-6): query params for the late/early report. */
export interface ILateEarlyReportQuery {
  /** `yyyy-MM-dd` inclusive range start. */
  from: string;
  /** `yyyy-MM-dd` inclusive range end. */
  to: string;
  departmentId?: string;
  employeeId?: string;
  scope?: LateEarlyScope;
}

/**
 * US-ATT-008 (§8, AC-4): the employee's monthly lateness score for the self-service
 * dashboard progress indicator ("X of N allowed lates used this month"). `allowedLates`
 * mirrors the policy `thresholdCount`.
 *   GET /api/v1/attendance/late-early/my-score?month=yyyy-MM
 *     -> ApiResponse<LatenessScoreDto>
 */
export interface ILatenessScore {
  /** The reported month, `yyyy-MM`. */
  yearMonth: string;
  /** Lates accumulated so far this month. */
  lateCount: number;
  /** The policy threshold = lates allowed before a deduction (= policy thresholdCount). */
  allowedLates: number;
  /** Early departures accumulated so far this month (shown alongside, §8). */
  earlyDepartureCount: number;
}

/**
 * US-ATT-008 (§8): format a minutes-late/early count into a compact badge label,
 * e.g. 20 -> "20 min", 90 -> "1h 30m". Falls back to "—" for non-positive values.
 */
export function formatLateBadgeMinutes(minutes: number | null | undefined): string {
  if (minutes == null || minutes <= 0) {
    return '—';
  }
  if (minutes < 60) {
    return `${minutes} min`;
  }
  return formatWorkMinutes(minutes);
}

/**
 * US-ATT-008 (§8): the lateness-score severity used to colour the progress bar —
 * green while under the allowance, amber on/over it. With no allowance configured
 * (allowedLates <= 0) any late renders amber.
 */
export function latenessScoreTone(score: Pick<ILatenessScore, 'lateCount' | 'allowedLates'>):
  | 'ok'
  | 'warn' {
  if (score.allowedLates <= 0) {
    return score.lateCount > 0 ? 'warn' : 'ok';
  }
  return score.lateCount >= score.allowedLates ? 'warn' : 'ok';
}

/**
 * US-ATT-008 (§8): clamped 0–100 percentage of the monthly late allowance used, for the
 * progress bar width. With no allowance (allowedLates <= 0) returns 100 when any late
 * exists (full bar = over budget), else 0.
 */
export function latenessUsedPercent(
  score: Pick<ILatenessScore, 'lateCount' | 'allowedLates'>,
): number {
  if (score.allowedLates <= 0) {
    return score.lateCount > 0 ? 100 : 0;
  }
  const pct = Math.round((score.lateCount / score.allowedLates) * 100);
  return Math.max(0, Math.min(100, pct));
}

// ─── US-ATT-009: Attendance Integration with Payroll ─────────────────────────

/**
 * US-ATT-009 (FR-1, FR-2, §7): one employee's attendance summary as the payroll module
 * consumes it for a period. The attendance-to-payroll feed — present/absent/LOP day
 * counts (decimals for half-days), late-deduction days, and approved overtime minutes.
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   GET /api/v1/attendance/payroll-data?month=yyyy-MM&employeeIds=<csv optional>
 *     -> ApiResponse<{ period, rows: AttendancePayrollRowDto[] }>
 *
 * Envelope is ApiResponse<T> = { success, data, message }; the service unwraps `.data`.
 */
export interface IAttendancePayrollRow {
  employeeId: string;
  /** The payroll period, `yyyy-MM`. */
  period: string;
  /** Scheduled working days in the period (BR-2). */
  totalWorkingDays: number;
  /** Days present including half-days (decimal). */
  totalPresentDays: number;
  /** Days absent (decimal). */
  totalAbsentDays: number;
  /** Loss-of-Pay days — unexcused absences feeding the LOP deduction (FR-7, BR-2). */
  lopDays: number;
  /** Late-arrival days converted to LOP per the late policy (BR-4). */
  lateDeductionDays: number;
  /** Approved overtime minutes only (FR-8, BR-5). */
  approvedOvertimeMinutes: number;
  /** Total net worked minutes across the period. */
  totalWorkMinutes: number;
  /** Breakdown by multiplier rate (jsonb passthrough, BR-3); shape owned by payroll. */
  overtimeMultiplierDetails: unknown;
}

/**
 * US-ATT-009 (FR-1): the payroll-data payload (the `data` of the envelope).
 *   GET /api/v1/attendance/payroll-data -> ApiResponse<PayrollDataResult>
 */
export interface IAttendancePayrollResult {
  /** The payroll period, `yyyy-MM`. */
  period: string;
  rows: IAttendancePayrollRow[];
}

/**
 * US-ATT-009 (FR-3, AC-4, §7 `attendance_period_lock`): the lock state of an attendance
 * period. While `isLocked` is true no clock-in/out, regularization, or modification is
 * allowed for the range (enforced server-side); payroll may proceed (BR-1).
 *
 * Backend contract (pinned — backend agent building the SAME contract):
 *   GET  /api/v1/attendance/period-lock?month=yyyy-MM        -> ApiResponse<PeriodLockDto | null>
 *   POST /api/v1/attendance/period-lock  body { periodStart, periodEnd } -> ApiResponse<PeriodLockDto>
 *   POST /api/v1/attendance/period-lock/{id}/unlock          -> ApiResponse<PeriodLockDto>
 *
 * `null` from the GET means the period has never been locked (no lock row exists yet).
 */
export interface IPeriodLock {
  id: string;
  /** Locked range start, `yyyy-MM-dd`. */
  periodStart: string;
  /** Locked range end, `yyyy-MM-dd`. */
  periodEnd: string;
  isLocked: boolean;
  /** HR officer who locked it (FR-4); null before any lock. */
  lockedBy?: string | null;
  /** When it was locked (UTC timestamptz, FR-4); null before any lock. */
  lockedAt?: string | null;
  /** HR officer who last unlocked it (FR-4, AC-5); null when never unlocked. */
  unlockedBy?: string | null;
  /** When it was last unlocked (UTC timestamptz); null when never unlocked. */
  unlockedAt?: string | null;
}

/**
 * US-ATT-009 (FR-5, AC-1): one employee row of the reconciliation view — the attendance
 * side of the attendance-vs-payroll comparison. The payroll-input columns are rendered
 * as a "pending payroll module" placeholder until that module exists (see §8 notes).
 *
 *   GET /api/v1/attendance/reconciliation?month=yyyy-MM
 *     -> ApiResponse<{ period, rows: ReconciliationRowDto[] }>
 */
export interface IReconciliationRow {
  employeeId: string;
  employeeName: string;
  /** Days present including half-days (decimal). */
  presentDays: number;
  /** Loss-of-Pay days feeding the LOP deduction (FR-7). */
  lopDays: number;
  /** Approved overtime minutes only (FR-8). */
  approvedOvertimeMinutes: number;
  /** Total net worked minutes across the period. */
  totalWorkMinutes: number;
}

/**
 * US-ATT-009 (FR-5): the reconciliation payload (the `data` of the envelope).
 *   GET /api/v1/attendance/reconciliation -> ApiResponse<ReconciliationResult>
 */
export interface IReconciliationResult {
  /** The reconciled period, `yyyy-MM`. */
  period: string;
  rows: IReconciliationRow[];
}

/**
 * US-ATT-009 (§8): a single step of the payroll-process stepper. Only the first step
 * ("Lock Attendance") is actionable in the attendance module; the rest are visual
 * placeholders until the payroll module is built (FR markers in the story §8).
 */
export interface IPayrollStep {
  /** Stable key for the step. */
  key: 'lock' | 'generate' | 'review' | 'finalize' | 'publish';
  /** Display label (§8 ordering: Lock → Generate → Review → Finalize → Publish). */
  label: string;
  /** Whether this step lives in the attendance module (only 'lock' today). */
  actionable: boolean;
}

/**
 * US-ATT-009 (§8): the ordered payroll-process steps for the visual stepper. Only the
 * "Lock Attendance" step is actionable here; the remainder are disabled placeholders
 * pending the payroll module.
 */
export const PAYROLL_PROCESS_STEPS: readonly IPayrollStep[] = [
  { key: 'lock', label: 'Lock Attendance', actionable: true },
  { key: 'generate', label: 'Generate Payroll', actionable: false },
  { key: 'review', label: 'Review', actionable: false },
  { key: 'finalize', label: 'Finalize', actionable: false },
  { key: 'publish', label: 'Publish', actionable: false },
];

/**
 * US-ATT-009 (§8): format a `yyyy-MM` period as a human month label, e.g.
 * "2026-05" -> "May 2026". Used in the lock banner and confirmation copy. Falls back
 * to the raw input when it can't be parsed.
 */
export function formatPeriodLabel(period: string): string {
  if (!/^\d{4}-\d{2}$/.test(period)) {
    return period;
  }
  const [y, m] = period.split('-').map(Number);
  const d = new Date(y, m - 1, 1);
  if (Number.isNaN(d.getTime())) {
    return period;
  }
  return d.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
}

/**
 * US-ATT-009 (§8): derive the inclusive calendar date range (`yyyy-MM-dd`) for a
 * `yyyy-MM` period — first and last day of that month. Used to build the lock POST body
 * (BR-8: a tenant-configurable cutoff may refine this; the default is the full month).
 */
export function periodDateRange(period: string): { start: string; end: string } {
  const [y, m] = period.split('-').map(Number);
  const start = new Date(y, m - 1, 1);
  const end = new Date(y, m, 0); // day 0 of next month = last day of this month
  const iso = (d: Date) =>
    `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d
      .getDate()
      .toString()
      .padStart(2, '0')}`;
  return { start: iso(start), end: iso(end) };
}

// ═══════════════════════════════════════════════════════════════════════════
// US-ATT-010: Attendance Dashboard & Reports for HR
// ═══════════════════════════════════════════════════════════════════════════
//
// Backend endpoints (pinned contract — backend agent building the SAME):
//   GET /attendance/dashboard?date=yyyy-MM-dd&scope=all|team
//        -> ApiResponse<DashboardKpiDto>
//   GET /attendance/dashboard/live-board?date=yyyy-MM-dd&scope=all|team
//        -> ApiResponse<{ date, rows: LiveBoardRowDto[] }>
//   GET /attendance/reports/department-comparison?month=yyyy-MM
//        -> ApiResponse<{ month, rows: DeptComparisonRowDto[] }>
//   GET /attendance/reports/custom?from&to&departmentId&locationId&shiftId&status
//        -> ApiResponse<{ from, to, rows: CustomReportRowDto[] }>
//   GET /attendance/reports/custom/export?from&to&format=csv|xlsx|pdf&...  -> blob
//   GET /attendance/reports/trends?months=12
//        -> ApiResponse<{ attendanceRate, lateArrivals, overtimeHours, absenteeismRate }>
//   GET/POST /attendance/reports/scheduled ; PUT/DELETE /attendance/reports/scheduled/{id}
//        -> ApiResponse<ScheduledReportConfigDto[]> | ApiResponse<ScheduledReportConfigDto>
//
// All envelopes are ApiResponse<T> = { success, data, message }; the service
// unwraps `.data`. SignalR is NOT available (§10) — the dashboard + live board
// fall back to ~30s polling.

/** Scope toggle: HR sees all employees; a Manager scopes to their team (BR-3/BR-4). */
export type AttendanceScope = 'all' | 'team';

/**
 * US-ATT-010 (AC-1, FR-1): today's attendance KPIs for the dashboard widget cards.
 * Backend computes these (Redis-cached, FR-7); the FE only renders.
 */
export interface IDashboardKpi {
  /** The KPI date, `yyyy-MM-dd`. */
  date: string;
  /** Active employees expected today (BR-1: active − full-day leave − holiday). */
  expectedHeadcount: number;
  /** Employees currently clocked in. */
  clockedIn: number;
  /** Expected but not yet clocked in. */
  pendingClockIn: number;
  /** Employees on full-day approved leave today. */
  onLeave: number;
  /** Not clocked in and not on leave. */
  absent: number;
  /** Live attendance percentage (BR-2: clockedIn / expected * 100). */
  attendancePercent: number;
}

/** A single row in the live attendance board (AC-2). */
export type LiveBoardStatus =
  | 'CLOCKED_IN'
  | 'NOT_CLOCKED_IN'
  | 'ON_LEAVE'
  | 'HOLIDAY';

export interface ILiveBoardRow {
  employeeId: string;
  employeeName: string;
  employeeNumber?: string;
  departmentName?: string;
  status: LiveBoardStatus;
  /** Clock-in timestamp (UTC) when status is CLOCKED_IN; the UI shows local time. */
  clockInAt?: string;
}

/** US-ATT-010 (AC-2): the live-board response envelope payload. */
export interface ILiveBoardResult {
  date: string;
  rows: ILiveBoardRow[];
}

/** US-ATT-010 (AC-3): a department's attendance rate for the comparison report. */
export interface IDeptComparisonRow {
  departmentId: string;
  departmentName: string;
  /** Attendance rate for the month, 0–100. */
  attendanceRatePct: number;
  employeeCount: number;
}

export interface IDeptComparisonResult {
  month: string;
  rows: IDeptComparisonRow[];
}

/** US-ATT-010 (AC-4, FR-4): custom report filters. */
export interface ICustomReportFilters {
  from: string; // yyyy-MM-dd
  to: string; // yyyy-MM-dd
  departmentId?: string | null;
  locationId?: string | null;
  shiftId?: string | null;
  status?: string | null;
}

/** US-ATT-010 (AC-4): a per-employee row in the custom date-range report. */
export interface ICustomReportRow {
  employeeId: string;
  employeeName: string;
  presentDays: number;
  absentDays: number;
  lateCount: number;
  overtimeMinutes: number;
  workMinutes: number;
}

export interface ICustomReportResult {
  from: string;
  to: string;
  rows: ICustomReportRow[];
}

/** Export format for the server-generated custom report (FR-5). */
export type CustomReportExportFormat = 'csv' | 'xlsx' | 'pdf';

/** US-ATT-010 (AC-5, FR-6): a single point in a 12-month trend series. */
export interface ITrendPoint {
  /** Period, `yyyy-MM`. */
  period: string;
  value: number;
}

/** US-ATT-010 (AC-5): the four trend series over the trailing N months. */
export interface ITrendsResult {
  attendanceRate: ITrendPoint[];
  lateArrivals: ITrendPoint[];
  overtimeHours: ITrendPoint[];
  absenteeismRate: ITrendPoint[];
}

/** US-ATT-010 (FR-8): scheduled-report config. */
export type ScheduledReportFrequency = 'DAILY' | 'WEEKLY' | 'MONTHLY';
export type ScheduledReportFormat = 'CSV' | 'XLSX' | 'PDF';

export interface IScheduledReportConfig {
  id?: string;
  /** Pre-built report type, e.g. 'daily-attendance', 'department-comparison'. */
  reportType: string;
  frequency: ScheduledReportFrequency;
  /** Saved filter configuration (free-form jsonb on the backend). */
  filters: ICustomReportFilters | Record<string, unknown>;
  /** Recipient identifiers (user IDs or emails — the backend resolves). */
  recipients: string[];
  /** Delivery time of day, `HH:mm`. */
  deliveryTime: string;
  format: ScheduledReportFormat;
  isActive: boolean;
}

// ─── Donut chart geometry (today's breakdown, §8) ────────────────────────────

/**
 * A donut segment for the dashboard "today's breakdown" chart. Built by
 * {@link buildDonutSegments} — a pure, unit-tested helper that emits SVG arc
 * paths over a ring (no charting library; mirrors the US-LV-012 SVG approach).
 */
export interface IDonutSegment {
  /** SVG `d` for the arc stroke segment. */
  path: string;
  color: string;
  label: string;
  value: number;
  /** 0–100 share of the whole. */
  percent: number;
}

/**
 * US-ATT-010 (§8): build donut-ring arc segments for a set of labelled values,
 * centred at (cx,cy) with the given radius and stroke width. Pure + unit-tested.
 * Each segment is a stroked arc (so the centre stays hollow). Returns [] when the
 * total is zero. A single non-zero value yields a full ring.
 */
export function buildDonutSegments(
  data: { label: string; value: number; color: string }[],
  cx: number,
  cy: number,
  radius: number,
): IDonutSegment[] {
  const total = data.reduce((s, d) => s + Math.max(0, d.value), 0);
  if (total <= 0) {
    return [];
  }
  let angle = -Math.PI / 2; // start at 12 o'clock
  return data
    .filter((d) => d.value > 0)
    .map((d) => {
      const frac = Math.max(0, d.value) / total;
      const sweep = frac * Math.PI * 2;
      const end = angle + sweep;
      const x1 = cx + radius * Math.cos(angle);
      const y1 = cy + radius * Math.sin(angle);
      const x2 = cx + radius * Math.cos(end);
      const y2 = cy + radius * Math.sin(end);
      const largeArc = sweep > Math.PI ? 1 : 0;
      const path =
        frac >= 0.999
          ? // full ring: draw as two half arcs so the stroke closes cleanly
            `M ${cx} ${cy - radius} A ${radius} ${radius} 0 1 1 ${cx - 0.01} ${
              cy - radius
            }`
          : `M ${round2(x1)} ${round2(y1)} A ${radius} ${radius} 0 ${largeArc} 1 ${round2(
              x2,
            )} ${round2(y2)}`;
      angle = end;
      return { path, color: d.color, label: d.label, value: d.value, percent: frac * 100 };
    });
}

/**
 * US-ATT-010 (AC-3, §8): color-code a department attendance rate — green > 90%,
 * amber 80–90%, red < 80%. Returns a hex usable directly as an SVG fill / inline
 * style. Pure + unit-tested.
 */
export function attendanceRateColor(pct: number): string {
  if (pct > 90) {
    return '#16a34a'; // green-600
  }
  if (pct >= 80) {
    return '#d97706'; // amber-600
  }
  return '#dc2626'; // red-600
}

/**
 * US-ATT-010 (AC-5): build SVG polyline points for a trend series scaled into a
 * width×height box. Mirrors the US-LV-012 line helper; higher value = smaller y.
 * Pure + unit-tested.
 */
export function buildTrendPoints(
  values: number[],
  width: number,
  height: number,
  globalMax: number,
): { x: number; y: number }[] {
  if (values.length === 0) {
    return [];
  }
  const max = globalMax > 0 ? globalMax : 1;
  const stepX = values.length > 1 ? width / (values.length - 1) : 0;
  return values.map((v, i) => ({
    x: values.length > 1 ? round2(i * stepX) : round2(width / 2),
    y: round2(height - (v / max) * height),
  }));
}

/** Stringify points for an SVG `points`/`d` attribute. */
export function trendPointsToString(points: { x: number; y: number }[]): string {
  return points.map((p) => `${p.x},${p.y}`).join(' ');
}

/** Max across a series of trend points (shared y-scale). */
export function trendMax(points: ITrendPoint[]): number {
  return points.reduce((m, p) => Math.max(m, p.value), 0);
}

/** Round to 2 dp (local to the US-ATT-010 helpers). */
function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

/** Current date as `yyyy-MM-dd` in the browser's local timezone. */
export function todayIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  const d = now.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * US-ATT-010 (§8): up-to-two-letter initials for a live-board avatar, derived from
 * the employee name. "Ada Lovelace" -> "AL"; single word -> first two letters.
 */
export function initialsOf(name: string): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '–';
  }
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// D1 slice 3 — WIRE CONTRACT → VIEW-MODEL MAPPERS
//
// Before this slice every attendance HTTP call named a hand-written `I…` type — an unchecked CAST, not a
// check. TypeScript accepted whatever the server actually sent. The two D1 slices that landed before
// this one found four live bugs that way, two of them CRITICAL.
//
// Each block below declares the REAL wire shape as a `Schema<'…'>` alias generated from the API's own
// OpenAPI document, plus an explicit mapper to the view model. Every optional wire field is defaulted
// deliberately: attendance drives PAY (overtime, late deductions) and APPROVALS, so an absent flag must
// never assert that work finished, that time was approved, or that a period is unlocked. Where the
// least-claiming value is not obvious, the reasoning is inline at the site.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════════════════════════
//  WIRE TYPES + MAPPERS — concern: clock-in / clock-out / status + monthly summaries
//  (US-ATT-001, US-ATT-002, US-ATT-007)
//
//  Requires at the TOP of this file:  import type { Schema } from '@core/api';
//
//  apiEnvelopeInterceptor already unwraps { success, data }, so these alias the INNER dto.
//  Every generated property is optional (Swashbuckle emits no `required`), so every field
//  is defaulted below. Non-obvious decisions, all deliberate:
//
//  RENAMES
//    AttendanceLogDto.id       -> IAttendanceLog.attendanceLogId
//    ClockOutResultDto.id      -> IClockOutResult.attendanceLogId
//
//  NO WIRE SOURCE (flagged, not silently nulled — see report OUT-OF-LANE #2)
//    IAttendanceLog.tenantId   -- the wire DTO carries no tenant id at all. Emitted as ''
//                                 to keep the interface satisfiable; the field must be
//                                 DELETED from IAttendanceLog (no component reads it).
//
//  DEFAULTS THAT ARE DECISIONS (which way each one fails)
//    requireGeolocation ?? false  -- fails OPEN in the UI on purpose. The server is the
//        authority: AttendanceService.ClockInAsync L142 / ClockOutAsync L343 reject a
//        coordinate-less punch with a typed 400 when the tenant requires geo, and the
//        server ITSELF defaults the flag to false when no settings row exists (L458).
//        Defaulting to `true` would hard-block clock-in client-side for tenants where geo
//        is optional -> no attendance record -> absent -> LOP. That failure is silent and
//        unrecoverable by the employee; failing open is loud (a server 400 the FE shows
//        verbatim) and cannot bypass anything.
//    isClockedIn ?? false         -- fails toward "offer the Clock In button". A wrong
//        `false` is caught by the backend's 409 already_clocked_in guard; a wrong `true`
//        would strand the employee on a timer they cannot clock out of.
//    ClockOutResult.status ?? 'ANOMALY' -- NEVER default to 'COMPLETE': that asserts a
//        clean finished day (Rule 1). ANOMALY is the module's own "flagged for review"
//        bucket, so an unknown value routes to a human instead of silently passing.
//    overtimeMinutes ?? null      -- null = "unknown", not 0 = "there was none". The card
//        hides the OT badge on null (clock-in.component L419 `?? 0`), so this claims nothing.
//    SummaryGenerationStatus.status ?? 'PENDING' -- never claim the Hangfire job finished
//        on a missing flag (same class as the payroll isComplete/isGenerating inversion).
//        WARNING: this is only safe once monthly-summary.component's poll is bounded —
//        its `takeWhile(s => s.status !== 'COMPLETED', true)` has no cap and each poll is a
//        full-tenant recompute. See report OUT-OF-LANE #4 (blocking companion fix).
//    DailyBreakdown.status ?? 'ABSENT' -- does not assert that work happened, and fails
//        loud (red cell) rather than silently green. Display-only: the pay-bearing counts
//        come from IEmployeeMonthlySummary, not from this cell.
//    source ?? 'WEB'              -- server-validated to WEB|MOBILE_WEB
//        (ClockInValidator L14) and the entity default is "WEB"; audit-only, unrendered.
//
//  UNMAPPED WIRE FIELDS (reported, deliberately NOT added to the view models — no screen
//  renders them): ClockStatusDto.lastCompleted, AttendanceLogDto.createdAt,
//  ClockOutResultDto.employeeId, DailyBreakdownDto.lateMinutes/earlyDepartureMinutes.
// ═══════════════════════════════════════════════════════════════════════════════

export type AttendanceLogWire = Schema<'AttendanceAttendanceLogDto'>;
export type ClockOutResultWire = Schema<'AttendanceClockOutResultDto'>;
export type ClockStatusWire = Schema<'AttendanceClockStatusDto'>;
export type MonthlySummaryBannerWire = Schema<'AttendanceMonthlySummaryBannerDto'>;
export type EmployeeMonthlySummaryWire = Schema<'AttendanceEmployeeMonthlySummaryDto'>;
export type MonthlySummaryResultWire = Schema<'AttendanceMonthlySummaryResult'>;
export type DailyBreakdownWire = Schema<'AttendanceDailyBreakdownDto'>;
export type EmployeeDailyBreakdownResultWire =
  Schema<'AttendanceEmployeeDailyBreakdownResult'>;
export type SummaryGenerationStatusWire = Schema<'AttendanceSummaryGenerationStatusDto'>;

// ─── narrowing helpers (guarded casts, never a blind `as`) ───────────────────

const ATTENDANCE_SOURCES: readonly AttendanceSource[] = ['WEB', 'MOBILE_WEB'];
const CLOCK_OUT_STATUSES: readonly ClockOutStatus[] = [
  'COMPLETE',
  'SHORT_DAY',
  'OVERTIME',
  'ANOMALY',
];
const DAILY_BREAKDOWN_STATUSES: readonly DailyBreakdownStatus[] = [
  'PRESENT',
  'ABSENT',
  'LEAVE',
  'HOLIDAY',
  'WEEKLY_OFF',
  'HALF_DAY',
];
const SUMMARY_GENERATION_STATUSES: readonly ISummaryGenerationStatus['status'][] = [
  'PENDING',
  'RUNNING',
  'COMPLETED',
];

/** Wire `source` is `string | null`; the server validates WEB|MOBILE_WEB. Fallback: 'WEB'. */
function narrowSource(value: string | null | undefined): AttendanceSource {
  return ATTENDANCE_SOURCES.includes(value as AttendanceSource)
    ? (value as AttendanceSource)
    : 'WEB';
}

/** Unknown/absent -> 'ANOMALY' (flag for review); never 'COMPLETE' (would assert a clean day). */
function narrowClockOutStatus(value: string | null | undefined): ClockOutStatus {
  return CLOCK_OUT_STATUSES.includes(value as ClockOutStatus)
    ? (value as ClockOutStatus)
    : 'ANOMALY';
}

/** Unknown/absent -> 'ABSENT'; never 'PRESENT' (would assert work happened). */
function narrowDailyStatus(value: string | null | undefined): DailyBreakdownStatus {
  return DAILY_BREAKDOWN_STATUSES.includes(value as DailyBreakdownStatus)
    ? (value as DailyBreakdownStatus)
    : 'ABSENT';
}

/** Unknown/absent -> 'PENDING'; never 'COMPLETED' (would claim the generation job finished). */
function narrowGenerationStatus(
  value: string | null | undefined,
): ISummaryGenerationStatus['status'] {
  return SUMMARY_GENERATION_STATUSES.includes(
    value as ISummaryGenerationStatus['status'],
  )
    ? (value as ISummaryGenerationStatus['status'])
    : 'PENDING';
}

// ─── US-ATT-001 / US-ATT-002: clock-in, clock-out, status ────────────────────

export function mapAttendanceLog(w: AttendanceLogWire): IAttendanceLog {
  return {
    // RENAME: the wire key is `id`.
    attendanceLogId: w.id ?? '',
    // NO WIRE SOURCE — AttendanceLogDto carries no tenant id. Placeholder only; see banner.
    tenantId: '',
    employeeId: w.employeeId ?? '',
    clockIn: w.clockIn ?? '',
    clockOut: w.clockOut ?? null,
    clockInLatitude: w.clockInLatitude ?? null,
    clockInLongitude: w.clockInLongitude ?? null,
    source: narrowSource(w.source),
    // Absent lateness must not claim a punctual arrival OR invent a penalty: false/0 is the
    // server's own on-time encoding, and the badge simply does not render.
    isLate: w.isLate ?? false,
    lateMinutes: w.lateMinutes ?? 0,
  };
}

/**
 * Shared child mapper — also reused by `ClockStatusDto.lastCompleted` (currently unmapped;
 * IClockStatus has no `lastCompleted` field — see report OUT-OF-LANE #5).
 */
export function mapClockOutResult(w: ClockOutResultWire): IClockOutResult {
  return {
    // RENAME: the wire key is `id`.
    attendanceLogId: w.id ?? '',
    clockIn: w.clockIn ?? '',
    clockOut: w.clockOut ?? '',
    totalWorkMinutes: w.totalWorkMinutes ?? 0,
    // null = unknown, NOT 0 = "no overtime was earned". OT feeds pay.
    overtimeMinutes: w.overtimeMinutes ?? null,
    status: narrowClockOutStatus(w.status),
    isEarlyDeparture: w.isEarlyDeparture ?? false,
    earlyDepartureMinutes: w.earlyDepartureMinutes ?? 0,
  };
}

export function mapClockStatus(w: ClockStatusWire): IClockStatus {
  return {
    isClockedIn: w.isClockedIn ?? false,
    clockedInAt: w.clockedInAt ?? null,
    // Fails OPEN by design — the server enforces the policy and defaults it to false itself.
    requireGeolocation: w.requireGeolocation ?? false,
    shiftName: w.shiftName ?? null,
    // NOTE: the wire type is date-time, the view model documents "HH:mm". Always null today
    // (backend: "always null until US-ATT-005"). See report OUT-OF-LANE #6.
    shiftStart: w.shiftStart ?? null,
  };
}

// ─── US-ATT-007: monthly summary + daily breakdown + generation ──────────────

export function mapMonthlySummaryBanner(
  w: MonthlySummaryBannerWire | undefined,
): IMonthlySummaryBanner {
  return {
    totalEmployees: w?.totalEmployees ?? 0,
    // NOT `?? 0`: unlike a count, a percentage has no neutral zero — 0% asserts
    // that nobody attended. An absent figure must claim nothing.
    averageAttendancePercent: w?.averageAttendancePercent ?? null,
    // 0 LOP days on an absent banner is the least-claiming value: it never invents a
    // deduction. The authoritative per-employee LOP still comes from each row.
    totalLopDays: w?.totalLopDays ?? 0,
  };
}

export function mapEmployeeMonthlySummary(
  w: EmployeeMonthlySummaryWire,
): IEmployeeMonthlySummary {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeeNumber: w.employeeNumber ?? null,
    departmentName: w.departmentName ?? null,
    // All counters default to 0: an absent counter must never inflate presence, overtime,
    // or LOP. 0 under-reports (visible as an obviously empty row) instead of over-paying
    // or over-deducting.
    presentDays: w.presentDays ?? 0,
    absentDays: w.absentDays ?? 0,
    lateCount: w.lateCount ?? 0,
    earlyDepartureCount: w.earlyDepartureCount ?? 0,
    workMinutes: w.workMinutes ?? 0,
    overtimeMinutes: w.overtimeMinutes ?? 0,
    leaveDays: w.leaveDays ?? 0,
    holidays: w.holidays ?? 0,
    weeklyOffs: w.weeklyOffs ?? 0,
    lopDays: w.lopDays ?? 0,
    generatedAt: w.generatedAt ?? '',
  };
}

export function mapMonthlySummaryResult(
  w: MonthlySummaryResultWire,
): IMonthlySummaryResult {
  return {
    yearMonth: w.yearMonth ?? '',
    rows: (w.rows ?? []).map(mapEmployeeMonthlySummary),
    banner: mapMonthlySummaryBanner(w.banner),
    // MUST stay null on absence: `notGenerated` (monthly-summary.component L404) keys off
    // `generatedAt == null` to offer on-demand generation. Inventing a timestamp would hide
    // the "not generated yet" state and show an empty table as if it were the real month.
    generatedAt: w.generatedAt ?? null,
  };
}

export function mapDailyBreakdown(w: DailyBreakdownWire): IDailyBreakdown {
  return {
    date: w.date ?? '',
    status: narrowDailyStatus(w.status),
    clockIn: w.clockIn ?? null,
    clockOut: w.clockOut ?? null,
    // null = no record for the day; the row renders no time range rather than "0m worked".
    workMinutes: w.workMinutes ?? null,
    // An absent flag must not claim the day was regularized/approved.
    isRegularized: w.isRegularized ?? false,
    isLate: w.isLate ?? false,
    isEarlyDeparture: w.isEarlyDeparture ?? false,
  };
}

export function mapEmployeeDailyBreakdownResult(
  w: EmployeeDailyBreakdownResultWire,
): IEmployeeDailyBreakdownResult {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    yearMonth: w.yearMonth ?? '',
    days: (w.days ?? []).map(mapDailyBreakdown),
  };
}

export function mapSummaryGenerationStatus(
  w: SummaryGenerationStatusWire,
): ISummaryGenerationStatus {
  return {
    yearMonth: w.yearMonth ?? '',
    // Fails OPEN: an unrecognised/absent status keeps the caller polling rather than
    // announcing success. Requires the bounded-poll companion fix (OUT-OF-LANE #4).
    status: narrowGenerationStatus(w.status),
    generatedAt: w.generatedAt ?? null,
  };
}

// ═════════════════════════════════════════════════════════════════════════════
//  D1 slice 3 — REGULARIZATIONS concern (US-ATT-003 submit/list, US-ATT-004
//  manager queue / approve / reject / bulk-approve).
//
//  Owns the shared child mapper `mapRegularizationDecision` (re-used by the
//  bulk result's `items[].decision`) plus `mapPendingRegularization` and
//  `mapBulkRegularizationItem`.
//
//  ── TWO MODEL EDITS ARE REQUIRED BEFORE THIS COMPILES (see FINDINGS) ────────
//   (1) `IRegularization` (~line 208): DELETE the line `  tenantId: string;`.
//       `AttendanceRegularizationDto` has no TenantId — the tenant is implicit
//       in the JWT/tenant interceptor. No component reads it (only 3 spec
//       fixtures set it). Inventing `tenantId: ''` would be a fabricated
//       tenant key, which is the one thing this codebase must never do.
//   (2) `IRegularizationDecisionDto.status` (~line 370): widen
//         'APPROVED' | 'REJECTED'   ->   'PENDING' | 'APPROVED' | 'REJECTED'
//       The backend genuinely returns Status = "PENDING" on an intermediate
//       multi-level approval step (RegularizationApprovalService.cs:443-450).
//
//  ── NON-OBVIOUS RENAMES ────────────────────────────────────────────────────
//   * IRegularization.regularizationId  <- wire `id`.   ****LIVE BUG TODAY****
//     Both `@for` blocks in regularization.component.ts (L167 desktop, L184
//     mobile) do `track req.regularizationId`. Untyped today, so every row
//     tracks `undefined`.
//   * IPendingRegularization.regularizationId <- wire `regularizationId`.
//     NO rename — verified field-for-field against
//     AttendancePendingRegularizationDto. The approval queue's row id is safe.
//
//  ── NON-OBVIOUS DEFAULTS (each one is a decision; direction stated) ────────
//   * `succeeded: w.succeeded ?? false` — an absent flag must NEVER claim an
//     approval happened. `onBulkSuccess` removes every `succeeded` row from the
//     queue and toasts "N request(s) approved"; defaulting true would silently
//     drop a request that was NOT approved. False keeps the row in the queue and
//     shows the (generic) error toast — recoverable, and visibly wrong.
//   * `status: 'PENDING'` fallback on both regularization statuses — an absent
//     or unrecognised status must never render the green APPROVED pill.
//   * `regularizationType` — passed through UNCHANGED when unrecognised (not
//     coerced to a member). `regularizationTypeLabel()` is an exhaustive switch
//     with no default, so an unknown type renders a BLANK Type cell rather than
//     confidently mislabelling a MISSED_CLOCK_IN as MISSED_BOTH. Same reasoning
//     as `mapPayslip`'s `pdfStatus`. Visibly wrong beats confidently wrong.
//   * `attendanceLogId` / `totalWorkMinutes` / `overtimeMinutes` /
//     `attendanceStatus` default to NULL, not 0/''. The backend leaves them null
//     on REJECT and on an intermediate approval step; `0` would read as a real
//     computed "0 minutes worked" and these feed payroll semantics.
//   * `decision` is left `undefined` (not a synthesised empty object) when the
//     wire omits it — a failed bulk item genuinely has no decision.
//
//  Backend value sources (READ-ONLY verification, 2026-09-01):
//   HRM.Domain/Entities/RegularizationType.cs
//     RegularizationType : MISSED_CLOCK_IN | MISSED_CLOCK_OUT | MISSED_BOTH
//     RegularizationStatus: PENDING | APPROVED | REJECTED | CANCELLED
//   HRM.Domain/Entities/RegularizationApprovalHistory.cs
//     decision `action` : "APPROVED" | "REJECTED"  (NOT "APPROVE"/"REJECT")
//   RegularizationApprovalService.cs:387,462
//     decision `attendanceStatus` : COMPLETE | SHORT_DAY | OVERTIME | ANOMALY
// ═════════════════════════════════════════════════════════════════════════════

export type RegularizationWire = Schema<'AttendanceRegularizationDto'>;
export type PendingRegularizationWire = Schema<'AttendancePendingRegularizationDto'>;
export type PendingRegularizationQueueWire =
  Schema<'AttendancePendingRegularizationQueueResult'>;
export type RegularizationDecisionWire = Schema<'AttendanceRegularizationDecisionDto'>;
export type BulkRegularizationItemWire = Schema<'AttendanceBulkRegularizationItemResult'>;
export type BulkApproveRegularizationWire =
  Schema<'AttendanceBulkApproveRegularizationResult'>;

/** The four values `HRM.Domain/Entities/RegularizationStatus` can emit. */
const REGULARIZATION_STATUS_VALUES: readonly RegularizationStatus[] = [
  'PENDING',
  'APPROVED',
  'REJECTED',
  'CANCELLED',
];

/**
 * Narrow the wire's bare `string | null` status onto the view-model union.
 *
 * Fallback is PENDING and that direction is deliberate: an absent, empty or
 * unrecognised status renders the amber "Pending" pill. It must never render
 * the green APPROVED pill — an employee who reads "Approved" stops chasing a
 * correction that was never actually applied, and the corrected day never
 * reaches payroll.
 */
function narrowRegularizationStatus(value: string | null | undefined): RegularizationStatus {
  return REGULARIZATION_STATUS_VALUES.includes(value as RegularizationStatus)
    ? (value as RegularizationStatus)
    : 'PENDING';
}

/**
 * US-ATT-003: an employee's own regularization request.
 * `POST /attendance/regularizations` (201) and `GET /attendance/regularizations`.
 *
 * RENAME: `id` -> `regularizationId`. This is the `track` key of both `@for`
 * blocks in regularization.component.ts; before this mapper every row tracked
 * `undefined`.
 *
 * `regularizationType` is passed through rather than coerced — see the banner.
 */
export function mapRegularization(w: RegularizationWire): IRegularization {
  return {
    // RENAME: the wire field is `id`. Bound to `@for … track req.regularizationId`.
    regularizationId: w.id ?? '',
    employeeId: w.employeeId ?? '',
    attendanceLogId: w.attendanceLogId ?? null,
    date: w.date ?? '',
    // Passed through, NOT coerced: an unknown type must render a blank label,
    // never a confidently wrong one (regularizationTypeLabel has no default arm).
    regularizationType: (w.regularizationType ?? '') as RegularizationType,
    requestedClockIn: w.requestedClockIn ?? null,
    requestedClockOut: w.requestedClockOut ?? null,
    reason: w.reason ?? '',
    // Absent/unknown => PENDING. Must never read as APPROVED.
    status: narrowRegularizationStatus(w.status),
    createdAt: w.createdAt ?? '',
  };
}

/**
 * US-ATT-004: one row of the manager's pending-approval queue.
 * `GET /attendance/regularizations/pending` -> `{ items, totalCount }`.
 *
 * NO RENAME on the id: the wire field is already `regularizationId`, verified
 * against `PendingRegularizationDto` (RegularizationApprovalDtos.cs). This id is
 * the row `track` key, the checkbox `selectedIds` key, and the path segment of
 * approve/reject/bulk-approve — it is the single most safety-critical field in
 * this slice, so the `?? ''` fallback is intentionally an EMPTY id rather than a
 * plausible-looking one: an empty id produces a 404/400 the manager sees, not a
 * silent action on the wrong record.
 */
export function mapPendingRegularization(
  w: PendingRegularizationWire,
): IPendingRegularization {
  return {
    regularizationId: w.regularizationId ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeePhoto: w.employeePhoto ?? null,
    date: w.date ?? '',
    // Passed through, NOT coerced — see mapRegularization.
    regularizationType: (w.regularizationType ?? '') as RegularizationType,
    requestedClockIn: w.requestedClockIn ?? null,
    requestedClockOut: w.requestedClockOut ?? null,
    reason: w.reason ?? '',
    submittedOn: w.submittedOn ?? '',
  };
}

/**
 * US-ATT-004: the outcome of a single approve/reject, and the `decision` embedded
 * in each bulk-approve item. SHARED child mapper — owned by this concern.
 *
 * `status` can be PENDING as well as APPROVED/REJECTED: an intermediate step of a
 * multi-level workflow records the approval but leaves the regularization pending
 * (RegularizationApprovalService.cs:443-450). Hence the widened union and the
 * PENDING fallback — never assert APPROVED on an absent status.
 *
 * The computed fields stay NULL on absence: the backend nulls them on REJECT and
 * on an intermediate step, and `0` would read as a genuine "0 minutes worked",
 * which is a payroll-visible claim.
 */
export function mapRegularizationDecision(
  w: RegularizationDecisionWire,
): IRegularizationDecisionDto {
  const status = narrowRegularizationStatus(w.status);
  return {
    regularizationId: w.regularizationId ?? '',
    // CANCELLED cannot arise from a decision; collapse it to PENDING so the VM
    // union stays the three values the backend actually emits here.
    status: status === 'CANCELLED' ? 'PENDING' : status,
    // Wire values are "APPROVED"/"REJECTED" (RegularizationApprovalHistory), NOT
    // "APPROVE"/"REJECT". Nothing renders this today; kept as a bare string.
    action: w.action ?? '',
    approvalLevel: w.approvalLevel ?? 0,
    attendanceLogId: w.attendanceLogId ?? null,
    totalWorkMinutes: w.totalWorkMinutes ?? null,
    overtimeMinutes: w.overtimeMinutes ?? null,
    attendanceStatus: w.attendanceStatus ?? null,
    comment: w.comment ?? null,
    actionedAt: w.actionedAt ?? '',
  };
}

/**
 * US-ATT-004 BR-7: one per-id result inside a bulk approve.
 *
 * `succeeded` defaults to FALSE. This is the load-bearing default of the slice:
 * `RegularizationApprovalsComponent.onBulkSuccess` removes every `succeeded` row
 * from the queue and counts it into the "N request(s) approved" toast. Defaulting
 * true on an absent flag would delete a request from the manager's queue while
 * claiming an approval that never happened, and there is no second screen that
 * would ever surface it again. False leaves the row in place and shows the error
 * toast — the manager retries and sees the truth.
 */
export function mapBulkRegularizationItem(
  w: BulkRegularizationItemWire,
): IBulkApproveItemResult {
  return {
    regularizationId: w.regularizationId ?? '',
    // Absent flag must NEVER claim success. See the doc comment above.
    succeeded: w.succeeded ?? false,
    // Genuinely absent on a failed item — do not synthesise an empty decision.
    decision: w.decision ? mapRegularizationDecision(w.decision) : undefined,
    error: w.error ?? undefined,
    errorCode: w.errorCode ?? undefined,
  };
}

/**
 * US-ATT-004 BR-7: `POST /attendance/regularizations/bulk-approve`.
 *
 * The counts are recomputed from the mapped items rather than trusted from the
 * wire, so `succeededCount` can never disagree with the rows the component
 * actually removes (and an absent `succeeded` flag cannot be laundered back into
 * a success by a wire-supplied count). `totalRequested` DOES come from the wire —
 * the backend de-duplicates ids, so it is the only honest source for "how many
 * distinct ids were processed".
 */
export function mapBulkApproveResult(
  w: BulkApproveRegularizationWire,
): IBulkApproveResult {
  const items = (w.items ?? []).map(mapBulkRegularizationItem);
  const succeededCount = items.filter((i) => i.succeeded).length;
  return {
    totalRequested: w.totalRequested ?? items.length,
    succeededCount,
    failedCount: items.length - succeededCount,
    items,
  };
}

// ─── D1 slice 3: SHIFTS & SHIFT ASSIGNMENT (US-ATT-005) — wire types + mappers ──
//
// Concern: getShifts / createShift / updateShift / cloneShift / assignShift / getResolvedShift.
// Owns the shared child types AttendanceShiftDto, AttendanceRotationDto, AttendanceRotationStepDto.
// (deleteShift is `.delete<void>` — untouched.)
//
// RENAMES: none. Every IShift field has an identically-named wire field; this concern is a pure
// optionality/narrowing migration, not a rename fix.
//
// NON-OBVIOUS DEFAULTS (each is a decision — the failure direction is stated at the call site):
//   isDefault      -> false   never let an absent flag paint every shift as the tenant default.
//   isActive       -> false   an absent flag must not claim a shift is live and schedulable.
//   type           -> 'SINGLE' guarded narrow; ROTATING is never inferred (it would open the
//                              rotation editor on a shift that has no rotation).
//   rotation       -> undefined  the backend sends `null` for SINGLE/FLEXIBLE (ShiftService.ToDto),
//                              even though the generated type omits `| null`. Truthiness-checked.
//   assignedCount  -> 0       never claim assignments that did not happen (assign toast reads it).
//
// KNOWN-BENIGN NUMERIC DEFAULTS (?? 0): breakDurationMinutes, gracePeriodMinutes,
// assignedEmployeeCount. These are non-nullable ints in C# (ShiftDto) and are always emitted; the
// `??` exists only because Swashbuckle marks every property optional. NOTE for reviewers: the edit
// form patches straight from the mapped IShift, so a wrongly-zeroed break/grace WOULD be written
// back on the next save — see ISSUE-410 (SHIFT-07).
//
// NOT MAPPED (wire fields with no IShift field — FLAGGED, not silently widened): standardWorkMinutes,
// minimumWorkMinutes, autoBreakMinutes, autoBreakThresholdMinutes, overtimeThresholdMinutes
// (the DF-56 per-shift work-minute overrides). No screen renders them and IShiftRequest cannot send
// them — see BUG-322 (they are wiped on every PUT).

export type ShiftWire = Schema<'AttendanceShiftDto'>;
export type ResolvedShiftWire = Schema<'AttendanceResolvedShiftDto'>;
export type RotationWire = Schema<'AttendanceRotationDto'>;
export type RotationStepWire = Schema<'AttendanceRotationStepDto'>;
export type AssignmentResultWire = Schema<'AttendanceAssignmentResultDto'>;

/**
 * Narrow the wire's `type: string | null` onto {@link ShiftType} (US-ATT-005 FR-1).
 *
 * The backend constant set is exactly SINGLE | ROTATING | FLEXIBLE
 * (HRM.Domain/Entities/Shift.cs `ShiftType`, enforced by `ShiftRequestValidator` via
 * `ShiftType.IsValid`), so the FE union already matches the emitted values 1:1 — this guard only
 * fires on a malformed/absent payload.
 *
 * FALLBACK = 'SINGLE', matching the entity default (`Shift.Type = ShiftType.Single`). Deliberately
 * NOT 'ROTATING' (it would render a rotation editor for a shift with no rotation) and not
 * 'FLEXIBLE' (it would hide the start/end columns on a shift that has them). Under 'SINGLE' an
 * unknown shift with no times renders "—" via `formatShiftTimes`, which claims nothing.
 */
function narrowShiftType(value: string | null | undefined): ShiftType {
  const v = (value ?? '').trim().toUpperCase();
  return v === 'SINGLE' || v === 'ROTATING' || v === 'FLEXIBLE' ? v : 'SINGLE';
}

/** Map one rotation step (FR-7). `order` is used only for relative sort on both sides. */
export function mapRotationStep(w: RotationStepWire): IRotationStep {
  return {
    order: w.order ?? 0,
    shiftId: w.shiftId ?? '',
    durationDays: w.durationDays ?? 0,
  };
}

/** Map the rotation pattern of a ROTATING shift (FR-7). */
export function mapRotation(w: RotationWire): IRotation {
  return {
    cycleLengthDays: w.cycleLengthDays ?? 0,
    referenceStartDate: w.referenceStartDate ?? '',
    steps: (w.steps ?? []).map(mapRotationStep),
  };
}

/**
 * Shared field logic for {@link mapShift} and {@link mapResolvedShift}. AttendanceResolvedShiftDto is
 * a structural superset of AttendanceShiftDto (C#: `ResolvedShiftDto : ShiftDto`), so the resolved
 * wire object is accepted here directly and only the three extra fields are added by the caller.
 */
function mapShiftFields(w: ShiftWire): IShift {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    type: narrowShiftType(w.type),
    // `HH:mm` or null (backend formats TimeOnly with "HH:mm"); null for FLEXIBLE (BR-8).
    startTime: w.startTime ?? null,
    endTime: w.endTime ?? null,
    breakDurationMinutes: w.breakDurationMinutes ?? 0,
    gracePeriodMinutes: w.gracePeriodMinutes ?? 0,
    minimumHours: w.minimumHours ?? null,
    // ISO day numbers 1=Mon..7=Sun. VERIFIED to agree with the backend, not assumed:
    // ShiftScheduleResolver.IsoDay / AttendanceSummaryService.IsoDay both map Sun=0 -> 7, and
    // ShiftRequestValidator rejects anything outside 1..7. `[]` renders "—" via formatWorkingDays.
    workingDays: w.workingDays ?? [],
    // An absent flag must not paint a shift with the tenant "Default" badge (BR-1, FR-5).
    isDefault: w.isDefault ?? false,
    // Least-claiming: absent => render "Inactive" rather than assert the shift is live/schedulable.
    // Nothing gates on this beyond the badge, so a wrong `false` is visible, not silent.
    isActive: w.isActive ?? false,
    // 0 understates the delete guard, but the guard is server-side (409 shift_in_use), so an absent
    // count cannot cause a wrongful delete — it can only show "0 assigned" until the next refresh.
    assignedEmployeeCount: w.assignedEmployeeCount ?? 0,
    // The generated type says `rotation?: AttendanceRotationDto` with no `| null`, but ShiftService
    // .ToDto emits `Rotation = null` for every non-ROTATING shift — hence the truthiness check, not
    // an `undefined` check. Absent => undefined, which is what the shift form/editor branches on.
    rotation: w.rotation ? mapRotation(w.rotation) : undefined,
  };
}

/** Map a shift definition (GET/POST/PUT /attendance/shifts, POST …/clone). */
export function mapShift(w: ShiftWire): IShift {
  return mapShiftFields(w);
}

/**
 * Map the shift resolved for an employee on a date (FR-7/AC-5). Reuses {@link mapShiftFields} for
 * every inherited field.
 *
 * `effectiveFrom` / `effectiveTo` are genuinely null on the wire when the resolution fell back to
 * the tenant default shift (C# `ResolvedShiftDto.EffectiveFrom` is `DateOnly?`). `IResolvedShift`
 * declares `effectiveFrom: string`, so absence is mapped to '' — a blank date that asserts no
 * window — rather than a fabricated date. See ISSUE-410 (SHIFT-05): the view model should be widened
 * to `string | null`, which is a models change outside this mapper.
 */
export function mapResolvedShift(w: ResolvedShiftWire): IResolvedShift {
  return {
    ...mapShiftFields(w),
    effectiveFrom: w.effectiveFrom ?? '',
    effectiveTo: w.effectiveTo ?? null,
    resolvedForDate: w.resolvedForDate ?? '',
  };
}

/**
 * Map the bulk-assign result (AC-2). `assignedCount` is rendered verbatim in the success toast AND
 * added to the row's `assignedEmployeeCount`, so it defaults to 0: an absent count must never let
 * the UI announce assignments that the backend did not make.
 */
export function mapAssignmentResult(w: AssignmentResultWire): IAssignmentResult {
  return {
    assignedCount: w.assignedCount ?? 0,
    employeeShiftIds: w.employeeShiftIds ?? [],
  };
}

// ═════════════════════════════════════════════════════════════════════════════
//  US-ATT-006 OVERTIME — wire types + mappers (D1 slice 3, "overtime" concern)
//
//  Covers: POST /attendance/overtime/pre-approval, GET /overtime/my,
//          GET /overtime/pending, POST /overtime/{id}/approve,
//          POST /overtime/{id}/reject, GET /overtime/report.
//  All six paths VERIFIED present in contracts/openapi/hrm-v1.json.
//
//  THIS SURFACE DRIVES PAY. Every default below is chosen so that an ABSENT wire
//  field can only ever UNDER-claim. Read the per-field comments before changing one.
//
//  Non-obvious defaults / decisions (full rationale at each site):
//   • approvedMinutes -> `?? null` (NEVER `?? overtimeMinutes`, NEVER `?? 0`).
//     null is the VM's real "not yet awarded" value and both components branch on it.
//   • multiplier      -> `?? Number.NaN` (NOT 0, NOT 1). Renders "—" via the existing
//     NaN branch in formatMultiplier(). See DECISION note below.
//   • status          -> guarded narrow, fallback 'PENDING'. Never APPROVED.
//   • type            -> guarded narrow, fallback 'AUTO_DETECTED'. Never PRE_APPROVED.
//   • id              -> `?? ''` and NEVER substituted from another field: an empty id
//     makes approve/reject 404 loudly instead of actioning the wrong record.
//   • report totals   -> zeroed object when the wire omits `totals` (under-claims).
//
//  Renames: NONE. Every wire key maps 1:1 onto the same VM key.
//  Wire fields with NO view-model home (deliberately NOT mapped — see FINDINGS):
//    OvertimeDto/QueueItemDto.dailyCapApplied, .weeklyCapExceeded  (ISSUE-079)
//    OvertimeReportRowDto.unapprovedMinutes, OvertimeReportTotals.unapprovedMinutes (ISSUE-080)
//    OvertimeQueueResult.totalCount (service already discards it; queue count is list length)
// ═════════════════════════════════════════════════════════════════════════════

export type OvertimeWire = Schema<'AttendanceOvertimeDto'>;
export type OvertimeQueueItemWire = Schema<'AttendanceOvertimeQueueItemDto'>;
export type OvertimeQueueResultWire = Schema<'AttendanceOvertimeQueueResult'>;
export type OvertimeDecisionWire = Schema<'AttendanceOvertimeDecisionDto'>;
export type OvertimeReportRowWire = Schema<'AttendanceOvertimeReportRowDto'>;
export type OvertimeReportTotalsWire = Schema<'AttendanceOvertimeReportTotals'>;
export type OvertimeReportResultWire = Schema<'AttendanceOvertimeReportResult'>;

/**
 * Wire `status` is `string | null`; the VM is the narrow {@link OvertimeStatus} union.
 * Backend literals verified in src/backend/HRM.Domain/Entities/OvertimeRecord.cs:
 * "PENDING" | "APPROVED" | "REJECTED" | "UNAPPROVED".
 *
 * FALLBACK = 'PENDING', and it is deliberate in two directions:
 *  - It can never read as APPROVED, so an absent/unknown status can never make an
 *    unapproved record look payroll-ready (BR-6: UNAPPROVED is never payroll-ready).
 *  - weeklyOvertimeMinutes() COUNTS PENDING toward the weekly cap and excludes
 *    REJECTED/UNAPPROVED, so 'PENDING' also makes the cap bar warn earlier rather
 *    than later. Falling back to 'UNAPPROVED' would silently shrink the bar.
 * An unrecognised (new backend) status also lands on 'PENDING' — never payable.
 */
const OVERTIME_STATUS_VALUES: readonly OvertimeStatus[] = [
  'PENDING',
  'APPROVED',
  'REJECTED',
  'UNAPPROVED',
];
function toOvertimeStatus(raw: string | null | undefined): OvertimeStatus {
  return OVERTIME_STATUS_VALUES.includes(raw as OvertimeStatus)
    ? (raw as OvertimeStatus)
    : 'PENDING';
}

/**
 * Wire `type` is `string | null`; the VM is the narrow {@link OvertimeType} union.
 * Backend literals: "AUTO_DETECTED" | "PRE_APPROVED" (OvertimeRecord.cs).
 *
 * FALLBACK = 'AUTO_DETECTED' — the LESS-claiming of the two. 'PRE_APPROVED' would
 * assert the employee obtained permission in advance (AC-2/FR-4), which is exactly
 * the claim BR-6 exists to police; "the system noticed it" claims nothing.
 */
const OVERTIME_TYPE_VALUES: readonly OvertimeType[] = ['AUTO_DETECTED', 'PRE_APPROVED'];
function toOvertimeType(raw: string | null | undefined): OvertimeType {
  return OVERTIME_TYPE_VALUES.includes(raw as OvertimeType)
    ? (raw as OvertimeType)
    : 'AUTO_DETECTED';
}

/**
 * The pay multiplier (1.5x / 2x / holiday rate, BR-3/BR-7). The VM types it as a
 * non-nullable `number`, so an absent wire value needs SOME number — and every
 * ordinary number is a lie:
 *   `?? 0` renders "0x"  -> reads as "this overtime is worth nothing".
 *   `?? 1` renders "1x"  -> silently pays straight time; the most dangerous option
 *                           because it looks like a legitimate rate.
 * NaN is the only value in `number` that cannot be misread as a rate, and
 * formatMultiplier() already has an explicit NaN branch that renders "—" (it
 * predates this mapper — the author anticipated an unknown multiplier). The FE
 * never does arithmetic with this value; it is display-only in both overtime
 * screens, and payroll multiplies server-side. If arithmetic is ever added, NaN
 * propagates visibly instead of producing a plausible wrong number.
 * See DECISION-OT-MULTIPLIER in the report: the honest fix is `multiplier: number | null`.
 */
function toOvertimeMultiplier(raw: number | null | undefined): number {
  return raw ?? Number.NaN;
}

/**
 * Map an `AttendanceOvertimeDto` (pre-approval submit, my-overtime list) to {@link IOvertime}.
 */
export function mapOvertime(w: OvertimeWire): IOvertime {
  return {
    // No fallback to any other field: components/my-overtime tracks rows by `ot.id`
    // and the approvals queue posts it to /overtime/{id}/approve. Empty fails loudly.
    id: w.id ?? '',
    employeeId: w.employeeId ?? '',
    attendanceLogId: w.attendanceLogId ?? null,
    // DatePipe renders '' as blank rather than throwing; do NOT substitute today.
    date: w.date ?? '',
    // 0, never a guess. Also the [max] of the manager's "adjust awarded minutes"
    // input — 0 blocks an adjustment rather than authorising invented minutes.
    overtimeMinutes: w.overtimeMinutes ?? 0,
    // PAY-CRITICAL. `?? null` preserves the wire's tri-state.
    //  - `?? w.overtimeMinutes` would award unapproved overtime, and my-overtime's
    //    displayMinutes()/detail row would report a PENDING record as approved.
    //  - `?? 0` would silently zero a genuinely APPROVED record (underpay) and
    //    render "0m" where the template's `!= null` branch expects "—".
    approvedMinutes: w.approvedMinutes ?? null,
    multiplier: toOvertimeMultiplier(w.multiplier),
    type: toOvertimeType(w.type),
    status: toOvertimeStatus(w.status),
    reason: w.reason ?? '',
    managerComment: w.managerComment ?? null,
    createdAt: w.createdAt ?? '',
  };
}

/** `GET /overtime/my` returns a bare array; preserve the existing `?? []` empty-list behaviour. */
export function mapOvertimeList(ws: OvertimeWire[] | null | undefined): IOvertime[] {
  return (ws ?? []).map(mapOvertime);
}

/**
 * Map one manager-queue row. `OvertimeQueueItemWire` is structurally a superset of
 * `OvertimeWire`, so the shared half is reused rather than duplicated — the id,
 * minutes and status defaults above apply identically here, which matters most in
 * this screen (a wrong id approves the wrong person's overtime).
 */
export function mapOvertimeQueueItem(w: OvertimeQueueItemWire): IOvertimeQueueItem {
  return {
    ...mapOvertime(w),
    // '' -> initials() renders '?' and the name cell is blank. Do NOT substitute
    // the employeeId: a GUID in the "Employee" column reads as a real identity.
    employeeName: w.employeeName ?? '',
    employeePhoto: w.employeePhoto ?? null,
    // Distinct from createdAt on the wire (submission vs record creation); keep them apart.
    submittedOn: w.submittedOn ?? '',
  };
}

/**
 * `GET /overtime/pending` returns `{ items, totalCount }`; the service already
 * projects to `items` only, so this replaces the existing `res?.items ?? []`.
 * `totalCount` is intentionally discarded — the queue badge counts the rendered list.
 */
export function mapOvertimeQueue(
  w: OvertimeQueueResultWire | null | undefined,
): IOvertimeQueueItem[] {
  return (w?.items ?? []).map(mapOvertimeQueueItem);
}

/**
 * Map an approve/reject decision. Backend emits status "APPROVED" | "REJECTED" here,
 * but the shared narrower's 'PENDING' fallback is the right failure mode: an absent
 * status must never read as APPROVED. On REJECT `approvedMinutes` is null on the wire
 * and stays null.
 */
export function mapOvertimeDecision(w: OvertimeDecisionWire): IOvertimeDecision {
  return {
    id: w.id ?? '',
    status: toOvertimeStatus(w.status),
    approvedMinutes: w.approvedMinutes ?? null,
    multiplier: toOvertimeMultiplier(w.multiplier),
    managerComment: w.managerComment ?? null,
    actionedAt: w.actionedAt ?? '',
  };
}

/**
 * One monthly-report row. All four minute columns are `?? 0`: this is an AGGREGATE
 * of records, so "the backend sent no number" and "no minutes in that bucket" are
 * both honestly rendered as 0m, and 0 is the under-claiming direction for pay.
 * NOTE: the wire also carries `unapprovedMinutes` (BR-6/ISSUE-080). IOvertimeReportRow
 * has no such field and the table renders no such column, so it is NOT mapped here —
 * see ISSUE-410 (ISSUE-OT-UNAPPROVED).
 */
export function mapOvertimeReportRow(w: OvertimeReportRowWire): IOvertimeReportRow {
  return {
    employeeId: w.employeeId ?? '',
    // '' keeps the row visible (and sortable) with a blank name rather than dropping it.
    employeeName: w.employeeName ?? '',
    approvedMinutes: w.approvedMinutes ?? 0,
    pendingMinutes: w.pendingMinutes ?? 0,
    rejectedMinutes: w.rejectedMinutes ?? 0,
    recordCount: w.recordCount ?? 0,
  };
}

/**
 * Report totals. The wire's `totals` is OPTIONAL, so an absent object becomes an
 * all-zero footer: visibly inconsistent with non-zero rows (someone notices) and
 * incapable of over-stating payable minutes. Summing the rows here instead would
 * invent a tenant-wide total the backend did not send.
 */
function mapOvertimeReportTotals(
  w: OvertimeReportTotalsWire | null | undefined,
): IOvertimeReportResult['totals'] {
  return {
    approvedMinutes: w?.approvedMinutes ?? 0,
    pendingMinutes: w?.pendingMinutes ?? 0,
    rejectedMinutes: w?.rejectedMinutes ?? 0,
    recordCount: w?.recordCount ?? 0,
  };
}

/**
 * `GET /overtime/report?month=yyyy-MM`. The wire's array key is `items` (NOT `rows`,
 * unlike the sibling attendance report DTOs) and the VM already agrees — verified, no rename.
 */
export function mapOvertimeReportResult(
  w: OvertimeReportResultWire,
): IOvertimeReportResult {
  return {
    // '' would render "No overtime records were found for ." in the empty state; the
    // component's own month() signal is the label source everywhere else, so '' is
    // preferable to echoing a month the server did not confirm.
    month: w.month ?? '',
    items: (w.items ?? []).map(mapOvertimeReportRow),
    totals: mapOvertimeReportTotals(w.totals),
  };
}

// ═══════════════════════════════════════════════════════════════════════════════
// WIRE TYPES + MAPPERS — concern: late/early policy & reports, lateness score,
// payroll feed, period locks, reconciliation (US-ATT-008 / US-ATT-009).
//
// Owns: AttendanceLatePolicyDto, AttendanceLateEarlyReportResult,
//       AttendanceLateEarlyRowDto, AttendanceLatenessScoreDto,
//       AttendanceAttendancePayrollResult, AttendanceAttendancePayrollRowDto,
//       AttendancePeriodLockDto, AttendanceReconciliationResult,
//       AttendanceReconciliationRowDto.
//
// RENAMES: none. Every field in this slice is name-for-name identical between the
// view models and the wire DTOs (verified field-by-field against hrm-v1.json).
// The value of these mappers is therefore ENTIRELY in the defaults and in the
// period-lock null path — not in renaming.
//
// NON-OBVIOUS DEFAULTS (each is also commented at its site):
//   · IPeriodLock.isLocked        -> `?? true`  (fail CLOSED: never claim "Open")
//   · ILatePolicy.isActive        -> `?? false` (absent must not switch deductions on)
//   · ILatePolicy.deductionDays   -> `?? 0`     (0 = no money moved)
//   · ILatePolicy.period          -> guarded narrow, fallback 'MONTHLY'
//   · ILateEarlyRow.isChronic     -> `?? false` (absent must not brand an employee)
//   · overtimeMultiplierDetails   -> passed through unchanged, never coerced
// ═══════════════════════════════════════════════════════════════════════════════

// ─── US-ATT-008: late policy ────────────────────────────────────────────────

export type LatePolicyWire = Schema<'AttendanceLatePolicyDto'>;

/**
 * US-ATT-008 (FR-4, BR-4). `AttendanceLatePolicyDto` is ALSO the PUT request body
 * (`PUT /api/v1/attendance/late-policy` accepts `AttendanceLatePolicyDto`), so the
 * VM round-trips cleanly and `updateLatePolicy` needs no request mapper.
 *
 * Direction of every default here is "the policy does the least": an absent field
 * must never invent a deduction, because `deductionDays` is money.
 */
export function mapLatePolicy(w: LatePolicyWire): ILatePolicy {
  return {
    // 0 is deliberately OUT OF RANGE for the policy form's `Validators.min(1)`, so an
    // absent threshold surfaces as a visible validation error HR must resolve rather
    // than silently persisting a fabricated number on the next save. Nothing in the FE
    // computes a deduction from this value — it is display + round-trip only.
    thresholdCount: w.thresholdCount ?? 0,
    // Money. 0 = "no day is deducted"; any non-zero fallback would invent a deduction.
    deductionDays: w.deductionDays ?? 0,
    // Guarded narrow, never a blind `as`. 'MONTHLY' is both the more permissive window
    // (the threshold resets more often) and what `latePolicyPeriodLabel` already falls
    // back to for anything that is not 'QUARTERLY'.
    period: w.period === 'QUARTERLY' ? 'QUARTERLY' : 'MONTHLY',
    // Absent must not claim employees are being notified.
    notificationOnLate: w.notificationOnLate ?? false,
    // Same reasoning as thresholdCount: 0 fails the form's `min(1)` and is visible.
    chronicThreshold: w.chronicThreshold ?? 0,
    // An absent flag must NEVER switch enforcement on — that would assert that late
    // deductions are live when we do not know that they are.
    isActive: w.isActive ?? false,
  };
}

// ─── US-ATT-008: late / early report ────────────────────────────────────────

export type LateEarlyRowWire = Schema<'AttendanceLateEarlyRowDto'>;
export type LateEarlyReportResultWire = Schema<'AttendanceLateEarlyReportResult'>;

export function mapLateEarlyRow(w: LateEarlyRowWire): ILateEarlyRow {
  return {
    employeeId: w.employeeId ?? '',
    // '' renders as an empty Employee cell; '—' would fabricate a label. The report is
    // keyed on employeeId, so an empty name is a visible data gap, not a wrong claim.
    employeeName: w.employeeName ?? '',
    // VM allows null and the template already renders `departmentName || '—'`.
    departmentName: w.departmentName ?? null,
    lateCount: w.lateCount ?? 0,
    totalLateMinutes: w.totalLateMinutes ?? 0,
    earlyDepartureCount: w.earlyDepartureCount ?? 0,
    totalEarlyMinutes: w.totalEarlyMinutes ?? 0,
    // FR-7: drives the amber `row-chronic` highlight, the "Chronic" badge and the CSV
    // "Yes/No" column. An absent flag must NOT brand an employee a chronic offender,
    // so this is the one boolean in this slice that fails OPEN (false = no accusation).
    isChronic: w.isChronic ?? false,
  };
}

export function mapLateEarlyReportResult(
  w: LateEarlyReportResultWire,
): ILateEarlyReportResult {
  return {
    from: w.from ?? '',
    to: w.to ?? '',
    rows: (w.rows ?? []).map(mapLateEarlyRow),
  };
}

// ─── US-ATT-008: lateness score ─────────────────────────────────────────────

export type LatenessScoreWire = Schema<'AttendanceLatenessScoreDto'>;

/**
 * US-ATT-008 (§8, AC-4). The VM is exactly the four wire fields — the "score",
 * the used-percentage and the amber/green tone are DERIVED in
 * `latenessScoreTone()` / `latenessUsedPercent()` and in the component, not
 * carried on the wire. Nothing is dropped here.
 */
export function mapLatenessScore(w: LatenessScoreWire): ILatenessScore {
  return {
    yearMonth: w.yearMonth ?? '',
    lateCount: w.lateCount ?? 0,
    // NOTE the knock-on: `latenessScoreTone`/`latenessUsedPercent` treat
    // `allowedLates <= 0` as "no allowance configured" and render amber / a full bar
    // on the first late. That is the existing, deliberate handling of an unset
    // allowance, so 0 is the consistent default — there is no null option on a
    // `number` VM field, and any positive fallback would invent an allowance the
    // policy never granted.
    allowedLates: w.allowedLates ?? 0,
    earlyDepartureCount: w.earlyDepartureCount ?? 0,
  };
}

// ─── US-ATT-009: attendance → payroll feed ──────────────────────────────────

export type AttendancePayrollRowWire = Schema<'AttendanceAttendancePayrollRowDto'>;
export type AttendancePayrollResultWire = Schema<'AttendanceAttendancePayrollResult'>;

/**
 * US-ATT-009 (FR-1, FR-2). All ten VM fields have a wire source and all ten wire
 * fields have a VM home — nothing dropped, nothing invented. Every numeric field
 * defaults to 0: a payroll feed row that is missing a count must read as "no days,
 * no minutes", never as an assumed workload.
 */
export function mapAttendancePayrollRow(
  w: AttendancePayrollRowWire,
): IAttendancePayrollRow {
  return {
    employeeId: w.employeeId ?? '',
    period: w.period ?? '',
    totalWorkingDays: w.totalWorkingDays ?? 0,
    totalPresentDays: w.totalPresentDays ?? 0,
    totalAbsentDays: w.totalAbsentDays ?? 0,
    lopDays: w.lopDays ?? 0,
    // Money (BR-4). 0 = no late-arrival day converted to LOP.
    lateDeductionDays: w.lateDeductionDays ?? 0,
    approvedOvertimeMinutes: w.approvedOvertimeMinutes ?? 0,
    totalWorkMinutes: w.totalWorkMinutes ?? 0,
    // Loosely-typed jsonb passthrough (wire `object | null`, VM `unknown`). Kept
    // verbatim and normalised only absent -> null; the shape is owned by payroll and
    // no attendance screen renders it, so coercing it here would be inventing structure.
    overtimeMultiplierDetails: w.overtimeMultiplierDetails ?? null,
  };
}

export function mapAttendancePayrollResult(
  w: AttendancePayrollResultWire,
): IAttendancePayrollResult {
  return {
    period: w.period ?? '',
    rows: (w.rows ?? []).map(mapAttendancePayrollRow),
  };
}

// ─── US-ATT-009: period lock ────────────────────────────────────────────────

export type PeriodLockWire = Schema<'AttendancePeriodLockDto'>;

/**
 * US-ATT-009 (FR-3, FR-4, AC-4/AC-5). The highest-stakes mapper in the module.
 *
 * CALL IT ONLY ON A NON-NULL BODY. `GET /attendance/period-lock` returns `null`
 * for "this period has never been locked", and that must stay `null` — running it
 * through this mapper would fabricate a lock row with an empty id and, worse, an
 * `isLocked: true` default. The service therefore uses
 * `map((res) => (res ? mapPeriodLock(res) : null))`, NOT `map(mapPeriodLock)`.
 * `lockPeriod` / `unlockPeriod` always return a body, so they map unconditionally.
 */
export function mapPeriodLock(w: PeriodLockWire): IPeriodLock {
  return {
    // No id can be invented. '' is honest, but see the FINDING: `confirmUnlock()`
    // bails silently on a falsy id, so an id-less lock row closes the modal with no
    // toast rather than erroring.
    id: w.id ?? '',
    periodStart: w.periodStart ?? '',
    periodEnd: w.periodEnd ?? '',
    // FAIL CLOSED. This flag decides whether the UI announces the period as "Open"
    // (offering "Lock Attendance", no locked banner, stepper step 1 unticked) or as
    // "Locked" (banner + "Unlock"). `?? false` on a present-but-malformed lock row
    // would tell HR the period is still editable and that payroll must not pull yet —
    // an assertion we cannot make from a missing field. `?? true` at worst offers an
    // Unlock that the backend rejects with a visible error toast; it never invites an
    // edit on a frozen period. The genuinely-unlocked case is a `null` BODY (handled
    // above by the service, not by this default), so this fail-closed choice does not
    // hide the legitimate "Lock Attendance" button on a never-locked period.
    isLocked: w.isLocked ?? true,
    lockedBy: w.lockedBy ?? null,
    lockedAt: w.lockedAt ?? null,
    unlockedBy: w.unlockedBy ?? null,
    unlockedAt: w.unlockedAt ?? null,
  };
}

// ─── US-ATT-009: reconciliation ─────────────────────────────────────────────

export type ReconciliationRowWire = Schema<'AttendanceReconciliationRowDto'>;
export type ReconciliationResultWire = Schema<'AttendanceReconciliationResult'>;

export function mapReconciliationRow(w: ReconciliationRowWire): IReconciliationRow {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    presentDays: w.presentDays ?? 0,
    lopDays: w.lopDays ?? 0,
    approvedOvertimeMinutes: w.approvedOvertimeMinutes ?? 0,
    totalWorkMinutes: w.totalWorkMinutes ?? 0,
  };
}

export function mapReconciliationResult(
  w: ReconciliationResultWire,
): IReconciliationResult {
  return {
    period: w.period ?? '',
    rows: (w.rows ?? []).map(mapReconciliationRow),
  };
}

// ═══════════════════════════════════════════════════════════════════════════
// US-ATT-010 wire types + mappers — HR DASHBOARD / LIVE BOARD / DEPT COMPARISON
//                                   / CUSTOM REPORT / TRENDS / SCHEDULED REPORTS
// ---------------------------------------------------------------------------
// Owns: AttendanceDashboardKpiDto, AttendanceLiveBoardResult(+RowDto),
//       AttendanceDeptComparisonResult(+RowDto), AttendanceCustomReportResult(+RowDto),
//       AttendanceTrendsResult(+AttendanceTrendPointDto), AttendanceScheduledReportConfigDto.
//
// FIELD NAMES: verified 1:1 against contracts/openapi/hrm-v1.json and the C# source
// (HRM.Application/Features/Attendance/DTOs/DashboardDtos.cs). There are NO renames in
// this concern — every VM field has an identically-named wire field. Every VM field
// also HAS a wire source (nothing is invented, nothing is dropped).
//
// WIRE-ONLY fields deliberately NOT surfaced (no screen renders them — see
// attendance-reports.component.ts custom-report table, which has 6 columns:
// Employee / Present / Absent / Late / Overtime / Worked):
//   AttendanceCustomReportRowDto.employeeNumber, .departmentName
// Widening ICustomReportRow to hold them would violate hard-rule #3.
//
// NON-OBVIOUS DEFAULTS (each is a decision; the failure direction is stated):
//  * Numeric KPI/report counters -> `?? 0`. The C# records declare these as
//    non-nullable int/decimal, so they are ALWAYS serialised; `?? 0` is defence
//    against a degraded payload, not a routine path. FAILURE DIRECTION: a KPI card
//    would render a confident "0" (see ISSUE-410 (F-06)) — there is no other option
//    without widening IDashboardKpi to `number | null`, which every KPI template
//    binding and the donut arithmetic would then have to handle.
//  * `rows` / series arrays -> `?? []`. Empty renders the component's own empty
//    state ("No data yet." / "Set a date range and click Generate.") which is the
//    honest read of "the server sent nothing".
//  * String scalars the UI does not render (`date`, `month`, `from`, `to`) -> `?? ''`.
//  * `ILiveBoardRow.status` -> PASSED THROUGH, never coerced. See below.
//  * `IScheduledReportConfig.frequency` / `.format` -> PASSED THROUGH, never coerced.
//  * `IScheduledReportConfig.isActive` -> `?? false` (an absent flag must NOT switch a
//    schedule on). Fails CLOSED: shows "Paused".
//
// ENUM-ISH UNIONS — the deliberate choice to pass through rather than fabricate.
// `LiveBoardStatus`, `ScheduledReportFrequency` and `ScheduledReportFormat` are narrow
// FE unions with no "unknown" member, while the wire types them `string | null`. Every
// consumer of these three already degrades safely on an unrecognised value:
//   attendance-dashboard.component.ts:429  statusLabel  = STATUS_META[s]?.label ?? s
//   attendance-dashboard.component.ts:433  statusPill   = STATUS_META[s]?.pill  ?? grey
//   attendance-dashboard.component.ts:438  clockInTime  = '—' unless status === 'CLOCKED_IN'
//   attendance-reports.component.ts:385    {{ s.frequency }} / {{ s.format }} rendered raw
// So an absent/unknown value renders BLANK-or-RAW with a NEUTRAL GREY pill — it reads as
// "unknown", and never as "present"/"active". Coercing instead (e.g. status -> 'NOT_CLOCKED_IN',
// frequency -> 'DAILY') would put a confident, actionable-but-false claim on screen. This is
// the same call made for `PayslipListItemDto.pdfStatus` in features/payroll/models/payslip.models.ts
// ("visibly wrong beats confidently wrong"). Raised as ISSUE-410 (F-07) (add explicit 'UNKNOWN'
// members) because a cast is still a cast.
//
// VOCABULARY VERIFIED AGAINST THE BACKEND (reading only — nothing edited):
//   LiveBoardStatus: HRM.Infrastructure/Services/AttendanceDashboardService.cs:615-628 emits
//     exactly "CLOCKED_IN" | "ON_LEAVE" | "HOLIDAY" | "NOT_CLOCKED_IN" — matches the FE union
//     value-for-value, no FE-only members, no missing members.
//   frequency / format: AttendanceDashboardService.cs:745-754 (ValidateScheduledDto) upper-cases
//     and REJECTS anything outside DAILY|WEEKLY|MONTHLY and CSV|XLSX|PDF, so a persisted row can
//     only ever carry a value in the FE union. An out-of-union value means a wire/serialisation
//     fault, not a legitimate state.
//   reportType: AttendanceDashboardService.cs:749 only checks IsNullOrWhiteSpace and Trim()s —
//     ANY non-blank string is accepted and echoed back verbatim. IScheduledReportConfig.reportType
//     is therefore correctly a bare `string`, not a union. (See ISSUE-410 (F-04) for the doc drift.)
//
// DEPENDENCIES ON OTHER CONCERNS: none. No shared child mapper is used or redeclared.
// ═══════════════════════════════════════════════════════════════════════════

export type DashboardKpiWire = Schema<'AttendanceDashboardKpiDto'>;
export type LiveBoardRowWire = Schema<'AttendanceLiveBoardRowDto'>;
export type LiveBoardResultWire = Schema<'AttendanceLiveBoardResult'>;
export type DeptComparisonRowWire = Schema<'AttendanceDeptComparisonRowDto'>;
export type DeptComparisonResultWire = Schema<'AttendanceDeptComparisonResult'>;
export type CustomReportRowWire = Schema<'AttendanceCustomReportRowDto'>;
export type CustomReportResultWire = Schema<'AttendanceCustomReportResult'>;
export type TrendPointWire = Schema<'AttendanceTrendPointDto'>;
export type TrendsResultWire = Schema<'AttendanceTrendsResult'>;
export type ScheduledReportConfigWire = Schema<'AttendanceScheduledReportConfigDto'>;

/**
 * US-ATT-010 (AC-1, FR-1) — `GET /attendance/dashboard`.
 * All seven VM fields map 1:1 onto identically-named wire fields; nothing renamed,
 * nothing dropped, nothing invented. `date` is not rendered by the dashboard (the
 * KPI card list at attendance-dashboard.component.ts:43-47 covers the five counters,
 * and `attendancePercent` is rendered separately at :141/:165), so `?? ''` is inert.
 */
export function mapDashboardKpi(w: DashboardKpiWire): IDashboardKpi {
  return {
    // Not rendered anywhere; the component tracks its own `date()` signal.
    date: w.date ?? '',
    // The five counters below drive the KPI cards AND the donut arithmetic
    // (buildDonutSegments). Non-nullable ints in C#, so `?? 0` is degraded-payload
    // defence only; it renders an indistinguishable "0" if it ever fires (ISSUE-410, F-06).
    expectedHeadcount: w.expectedHeadcount ?? 0,
    clockedIn: w.clockedIn ?? 0,
    pendingClockIn: w.pendingClockIn ?? 0,
    onLeave: w.onLeave ?? 0,
    absent: w.absent ?? 0,
    // Rendered as "{{ attendancePercent | number:'1.0-1' }}%" and in the donut hub.
    // Server-computed (clockedIn / expectedHeadcount * 100); the FE never recomputes it.
    attendancePercent: w.attendancePercent ?? 0,
  };
}

/**
 * US-ATT-010 (AC-2, FR-2) — one row of the live attendance board.
 * `status` drives the coloured presence chip and is PASSED THROUGH unchanged: an
 * absent value becomes `''`, which misses the STATUS_META lookup and renders an
 * empty label on the neutral-grey pill, and `clockInTime()` returns '—' because the
 * status is not 'CLOCKED_IN'. Coercing to 'NOT_CLOCKED_IN' would print a confident
 * "Not Clocked In" that HR could act on; coercing to anything green is unthinkable.
 */
export function mapLiveBoardRow(w: LiveBoardRowWire): ILiveBoardRow {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    // Optional on the VM AND nullable on the wire: keep `undefined`, do not invent ''.
    employeeNumber: w.employeeNumber ?? undefined,
    departmentName: w.departmentName ?? undefined,
    // Guarded cast, documented fallback: unknown/absent => '' => grey chip, blank label.
    status: (w.status ?? '') as LiveBoardStatus,
    // Only read when status === 'CLOCKED_IN'; `undefined` renders '—'.
    clockInAt: w.clockInAt ?? undefined,
  };
}

/** US-ATT-010 (AC-2, FR-2) — `GET /attendance/dashboard/live-board`. */
export function mapLiveBoardResult(w: LiveBoardResultWire): ILiveBoardResult {
  return {
    // Not rendered; the board header uses the component's own `date()` signal.
    date: w.date ?? '',
    // `[]` renders the board's empty state — the honest read of "no rows sent".
    rows: (w.rows ?? []).map(mapLiveBoardRow),
  };
}

/** US-ATT-010 (AC-3, FR-3) — one department row of the comparison report. */
export function mapDeptComparisonRow(w: DeptComparisonRowWire): IDeptComparisonRow {
  return {
    departmentId: w.departmentId ?? '',
    departmentName: w.departmentName ?? '',
    // Feeds attendanceRateColor() (>90 green / 80-90 amber / <80 red) and a clamped
    // bar width. A missing rate therefore paints RED, not green — the safe direction:
    // it under-claims department performance rather than certifying it.
    attendanceRatePct: w.attendanceRatePct ?? 0,
    employeeCount: w.employeeCount ?? 0,
  };
}

/** US-ATT-010 (AC-3, FR-3) — `GET /attendance/reports/department-comparison`. */
export function mapDeptComparisonResult(
  w: DeptComparisonResultWire,
): IDeptComparisonResult {
  return {
    // Not rendered; the component owns the `deptMonth()` signal it queried with.
    month: w.month ?? '',
    rows: (w.rows ?? []).map(mapDeptComparisonRow),
  };
}

/**
 * US-ATT-010 (AC-4, FR-4) — one employee row of the custom date-range report.
 * The wire also carries `employeeNumber` and `departmentName`; the report table
 * renders neither, so they are intentionally not surfaced (hard-rule #3).
 */
export function mapCustomReportRow(w: CustomReportRowWire): ICustomReportRow {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    // Day counts and minute totals; `?? 0` renders "0"/"0m". These are display-only
    // (this report feeds no pay calculation — see mapAttendancePayrollRow for that).
    presentDays: w.presentDays ?? 0,
    absentDays: w.absentDays ?? 0,
    lateCount: w.lateCount ?? 0,
    overtimeMinutes: w.overtimeMinutes ?? 0,
    workMinutes: w.workMinutes ?? 0,
  };
}

/** US-ATT-010 (AC-4, FR-4) — `GET /attendance/reports/custom`. */
export function mapCustomReportResult(
  w: CustomReportResultWire,
): ICustomReportResult {
  return {
    // Not rendered; the export filename is built from the component's own filters().
    from: w.from ?? '',
    to: w.to ?? '',
    rows: (w.rows ?? []).map(mapCustomReportRow),
  };
}

/**
 * US-ATT-010 (AC-5, FR-6) — one point of a trend series.
 * Wire field names are `period` / `value` — identical to ITrendPoint. `period` is the
 * x-axis category label and `value` the y-value; a renamed field here would silently
 * flat-line the SVG at y = chartH.
 */
export function mapTrendPoint(w: TrendPointWire): ITrendPoint {
  return {
    period: w.period ?? '',
    value: w.value ?? 0,
  };
}

/**
 * US-ATT-010 (AC-5, FR-6) — `GET /attendance/reports/trends`.
 * The four series keys (`attendanceRate` / `lateArrivals` / `overtimeHours` /
 * `absenteeismRate`) match TREND_SERIES in attendance-reports.component.ts:48-53
 * exactly. `?? []` per series: an empty series yields `points: ''` and no dots, i.e.
 * an empty chart card — which is what the data actually says.
 */
export function mapTrendsResult(w: TrendsResultWire): ITrendsResult {
  return {
    attendanceRate: (w.attendanceRate ?? []).map(mapTrendPoint),
    lateArrivals: (w.lateArrivals ?? []).map(mapTrendPoint),
    overtimeHours: (w.overtimeHours ?? []).map(mapTrendPoint),
    absenteeismRate: (w.absenteeismRate ?? []).map(mapTrendPoint),
  };
}

/**
 * US-ATT-010 (FR-8) — a scheduled-report configuration (response direction).
 *
 * `isActive` is the gate the Hangfire job filters on (HRM.Api/Jobs/ScheduledReportJob.cs:81
 * `.Where(c => c.IsActive)`), so an ABSENT flag must never read as "on": `?? false` shows
 * "Paused". Fails closed — the worst case is HR re-enabling a schedule that was already on,
 * never a schedule that silently keeps mailing while the UI says it is paused.
 *
 * `frequency` / `format` are passed through with a guarded cast rather than coerced to a
 * union member: both are rendered raw ("{{ s.frequency }} · {{ s.deliveryTime }} · {{ s.format }}"),
 * so an unknown value shows itself instead of fabricating a delivery cadence. Defaulting
 * frequency to 'DAILY' would tell HR a report arrives every morning when the backend's
 * IsDue() switch returns `false` for any unrecognised frequency and it never sends at all.
 */
export function mapScheduledReportConfig(
  w: ScheduledReportConfigWire,
): IScheduledReportConfig {
  return {
    // Wire `id` is nullable (absent on create). `undefined` keeps the VM's optional
    // shape and keeps deleteSchedule()'s `if (!config.id) return;` guard honest.
    id: w.id ?? undefined,
    // Free-form on both sides; the backend accepts any non-blank string (no union).
    reportType: w.reportType ?? '',
    // Guarded casts, pass-through fallback — see the banner note above.
    frequency: (w.frequency ?? '') as ScheduledReportFrequency,
    format: (w.format ?? '') as ScheduledReportFormat,
    // Wire is `{ [key: string]: unknown } | null` (C# Dictionary<string, object?>),
    // which is assignable to the VM's `Record<string, unknown>` arm. `{}` == "no
    // saved filters", matching the backend's own `FiltersJson = "{}"` default.
    filters: w.filters ?? {},
    // `[]` renders "0 recipient(s)" and the create form refuses to submit on empty —
    // the least-claiming default. NOTE: these are backend GUIDs, not emails (BUG-319).
    recipients: w.recipients ?? [],
    // Rendered raw between two "·" separators. `''` shows a visible gap rather than
    // asserting a delivery time the schedule does not have.
    deliveryTime: w.deliveryTime ?? '',
    // Absent flag must NOT switch a schedule on.
    isActive: w.isActive ?? false,
  };
}

/**
 * US-ATT-010 (FR-8) — request direction for POST/PUT `…/reports/scheduled`.
 *
 * The VM is currently posted straight through as the request body. Every VM key is
 * name-identical to `AttendanceScheduledReportConfigDto`, so no key is silently wrong —
 * this function exists so the COMPILER proves that, and keeps proving it after the next
 * `npm run api:types` regeneration. It is a shape adapter only: it deliberately does not
 * transform `recipients`, because the email-vs-GUID defect (BUG-319) is a UI/backend
 * decision, not something a mapper may paper over.
 */
export function toScheduledReportConfigWire(
  vm: IScheduledReportConfig,
): ScheduledReportConfigWire {
  return {
    // Omitted-as-null on create; the backend binds `Guid? Id`.
    id: vm.id ?? null,
    reportType: vm.reportType,
    frequency: vm.frequency,
    format: vm.format,
    filters: vm.filters as Record<string, unknown>,
    recipients: vm.recipients,
    deliveryTime: vm.deliveryTime,
    isActive: vm.isActive,
  };
}
