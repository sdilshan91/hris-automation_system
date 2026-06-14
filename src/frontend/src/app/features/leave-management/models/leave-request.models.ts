/**
 * US-LV-003: Leave Request (Apply for Leave) models matching the backend API contract.
 *
 * Backend endpoints (backend agent building in parallel -- assumed contract):
 *   POST /api/v1/leaves        - create a leave request, returns ILeaveRequest (status 'Pending')
 *   GET  /api/v1/leaves/mine   - current employee's own leave requests
 *
 * For the inline balance preview, the existing leave-entitlement balance endpoint
 * is reused (see ILeaveBalance below).
 */

/** Half-day session selector (AC-4). Null when not a half-day request. */
export type HalfDaySession = 'AM' | 'PM';

/** Leave request lifecycle status (matches data requirements §7) */
export type LeaveRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

/**
 * Leave request entity returned by the API (FR-5, FR-6).
 * `attachmentUrls` mirrors the `attachment_urls (text[])` column.
 */
export interface ILeaveRequest {
  leaveRequestId: string;
  tenantId: string;
  employeeId: string;
  leaveTypeId: string;
  /** Denormalized for list display so no extra lookup is needed. */
  leaveTypeName: string;
  /** Denormalized hex color for the status/type badge. */
  leaveTypeColor: string;
  startDate: string;
  endDate: string;
  isHalfDay: boolean;
  halfDaySession: HalfDaySession | null;
  totalDays: number;
  reason: string;
  status: LeaveRequestStatus;
  requestedAt: string;
  attachmentUrls: string[];
}

/**
 * Request payload for creating a leave request (FR-5).
 * The backend is the source of truth for validation (overlap, balance,
 * holiday-adjusted day count); the frontend mirrors the ACs for fast feedback only.
 */
export interface ICreateLeaveRequest {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  isHalfDay: boolean;
  halfDaySession: HalfDaySession | null;
  reason: string;
  /**
   * Attachment URLs. The actual blob upload backend is DEFERRED (US-LV NFR-3);
   * the frontend submits already-hosted URLs / metadata the backend accepts.
   */
  attachments: string[];
}

/**
 * Current leave balance for a single leave type (FR-2, AC-2).
 *
 * Sourced from the leave-entitlement/balance endpoint. The frontend reuses
 * whatever balance value is available; if the backend later returns a richer
 * balance object, only this interface and the service mapping change.
 */
export interface ILeaveBalance {
  leaveTypeId: string;
  /** Total entitlement for the current leave year. */
  entitlementDays: number;
  /** Days already used / pending. */
  usedDays: number;
  /** Remaining = entitlement - used. May be negative if the type allows it. */
  remainingDays: number;
}

/**
 * Client-side projection shown inline as the user picks a type + date range (AC-2, AC-6).
 * `requestedDays` is computed client-side (weekends excluded); the backend remains
 * authoritative and may adjust for public holidays (AC-6).
 */
export interface IBalanceProjection {
  remainingDays: number;
  requestedDays: number;
  projectedRemaining: number;
  /** True when projectedRemaining < 0 and the type does not allow negative balance. */
  insufficient: boolean;
}

/** Error response shape from the backend for leave request operations. */
export interface ILeaveRequestErrorResponse {
  message: string;
  /** Distinguishes overlap (AC-5), insufficient balance (AC-2), document-required (AC-3). */
  code?: 'overlap' | 'insufficient_balance' | 'document_required' | string;
}

/**
 * US-LV-010: Cancellation request body.
 *
 *   POST /api/v1/leaves/{id}/cancel  body { reason }  -> ILeaveRequest (status 'Cancelled')
 *
 * `reason` is MANDATORY for approved requests (BR-5) — the confirm dialog disables
 * submit until it is non-empty for an approved request — and OPTIONAL for pending.
 * The backend remains the authority on eligibility (started/past, payroll-locked).
 */
export interface ICancelLeaveRequest {
  /** Cancellation reason. Required for approved requests (BR-5), optional for pending. */
  reason: string;
}

/**
 * Typed error body for the cancel action (AC-3, AC-4 + concurrency AC).
 * `message` is shown verbatim via toast; `code` is the machine-readable discriminator.
 *   - 400 `code: 'already_started'`  -> "Cannot cancel leave that has already started..." (AC-3)
 *   - 400 `code: 'payroll_locked'`   -> payroll-locked message (AC-4)
 *   - 409 (any code)                 -> already actioned by a manager; refresh the list
 */
export interface ICancelLeaveErrorResponse {
  message: string;
  code?: 'already_started' | 'payroll_locked' | 'already_actioned' | string;
}

/**
 * Result of evaluating whether a request can be cancelled by the employee (§8, BR-2, BR-3).
 * Drives the Cancel button's enabled/disabled state + the explanatory tooltip.
 */
export interface ICancelEligibility {
  /** True when the Cancel action should be enabled. */
  eligible: boolean;
  /** When `eligible` is false, the reason shown as a tooltip on the disabled button. */
  reason: string;
}

/**
 * Pure helper (§8, BR-2/BR-3, AC-1/AC-3): decide if a leave request is cancellable
 * by the requesting employee, based ONLY on fields the API provides (status + dates).
 *
 * Eligible when:
 *  - status is 'Pending' (always cancellable before action), OR
 *  - status is 'Approved' AND the leave start date is strictly in the future
 *    (has not started yet) — AC-1/AC-2/AC-3.
 *
 * Ineligible (with an explanatory tooltip) when:
 *  - already started / in the past (Approved with startDate <= today) — AC-3,
 *  - already Rejected or Cancelled (terminal) — BR-2.
 *
 * Payroll-lock (AC-4) is NOT detectable from these fields, so it is enforced by the
 * backend and surfaced as a 400 error on submit; we do not pre-block for it here.
 * `today` is injectable for deterministic tests; defaults to the current date.
 */
export function evaluateCancelEligibility(
  req: Pick<ILeaveRequest, 'status' | 'startDate'>,
  today: Date = new Date(),
): ICancelEligibility {
  if (req.status === 'Cancelled') {
    return { eligible: false, reason: 'This request has already been cancelled.' };
  }
  if (req.status === 'Rejected') {
    return { eligible: false, reason: 'Rejected requests cannot be cancelled.' };
  }
  if (req.status === 'Pending') {
    return { eligible: true, reason: '' };
  }
  // Approved: only cancellable if the leave has not yet started.
  const start = new Date(req.startDate + 'T00:00:00');
  const todayMidnight = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  if (isNaN(start.getTime())) {
    return { eligible: false, reason: 'This request cannot be cancelled.' };
  }
  if (start <= todayMidnight) {
    return {
      eligible: false,
      reason: 'This leave has already started or passed. Please contact HR to cancel.',
    };
  }
  return { eligible: true, reason: '' };
}

/** Half-day session display options for the form select (AC-4). */
export const HALF_DAY_SESSION_OPTIONS: { value: HalfDaySession; label: string }[] = [
  { value: 'AM', label: 'Morning (AM)' },
  { value: 'PM', label: 'Afternoon (PM)' },
];

/** Status badge styling tokens (Notion-style muted pills). */
export const STATUS_BADGE_CLASSES: Record<LeaveRequestStatus, string> = {
  Pending: 'bg-amber-50 text-amber-700 ring-amber-200',
  Approved: 'bg-green-50 text-green-700 ring-green-200',
  Rejected: 'bg-red-50 text-red-700 ring-red-200',
  Cancelled: 'bg-neutral-100 text-neutral-500 ring-neutral-200',
};

/**
 * Pure helper: count working days in an inclusive date range, excluding weekends
 * (Sat/Sun). Public holidays are excluded by the backend (AC-6) and reflected in
 * the returned `totalDays`; the client estimate ignores them.
 *
 * Returns 0 for an invalid range (start after end) or unparseable dates.
 * For a half-day request the caller multiplies the single-day result by 0.5.
 */
export function countWorkingDays(startDate: string, endDate: string): number {
  if (!startDate || !endDate) {
    return 0;
  }
  const start = new Date(startDate + 'T00:00:00');
  const end = new Date(endDate + 'T00:00:00');
  if (isNaN(start.getTime()) || isNaN(end.getTime()) || start > end) {
    return 0;
  }
  let count = 0;
  const cursor = new Date(start);
  while (cursor <= end) {
    const day = cursor.getDay(); // 0 = Sun, 6 = Sat
    if (day !== 0 && day !== 6) {
      count++;
    }
    cursor.setDate(cursor.getDate() + 1);
  }
  return count;
}

/**
 * Pure helper: build a balance projection (AC-2).
 * `negativeAllowed` suppresses the insufficient flag for leave types that permit
 * negative balances (e.g. Unpaid Leave).
 */
export function buildProjection(
  remainingDays: number,
  requestedDays: number,
  negativeAllowed: boolean,
): IBalanceProjection {
  const projectedRemaining = remainingDays - requestedDays;
  return {
    remainingDays,
    requestedDays,
    projectedRemaining,
    insufficient: !negativeAllowed && requestedDays > 0 && projectedRemaining < 0,
  };
}
