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
  tenantId: string;
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
  status: 'APPROVED' | 'REJECTED';
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
