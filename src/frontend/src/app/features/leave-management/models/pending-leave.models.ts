/**
 * US-LV-004: Manager pending-leave-queue models. RECONCILED with the actual backend
 * `PendingLeaveRequestDto` / `PendingLeaveQueueResult` (see vault Frontend (US-LV-004)).
 *
 * Backend endpoint:
 *   GET /api/v1/leaves/pending
 *     ?leaveTypeId&employeeId&startDate&endDate&sortBy&sortAscending&page&pageSize
 *   -> ApiResponse<{ items, totalCount, page, pageSize }>  (the service unwraps `.data`)
 *
 * `apiBaseUrl` already includes `/api/v1`, so the resource base is `${apiBaseUrl}/leaves`.
 *
 * CONTRACT GAP (flagged): the backend `currentBalance` is a SCALAR (remaining days only) and
 * carries NO entitlement. The §8 pill color (green >50% / yellow 20-50% / red <20% of
 * entitlement) needs entitlement to compute a ratio. The FE therefore reads an OPTIONAL
 * `entitlementDays` and only color-codes by ratio when it is present; otherwise it falls back to
 * a sign-based tier (>=0 neutral, <0 red). TODO(backend): add `entitlementDays` to the DTO.
 */

/**
 * A single pending leave request in the manager's approval queue (FR-2).
 *
 * All fields are denormalized by the backend so the queue needs no extra
 * lookups: leave-type name/color, employee name/photo, and the current
 * real-time balance for the requested leave type (BR-4).
 */
export interface IPendingLeaveRequest {
  /** Leave request id (used for detail-panel open + track-by). */
  requestId: string;
  /**
   * Employee id of the requester. Used to round-trip the server-side employee
   * filter (AC-3) and to build the employee filter option list from results.
   */
  employeeId: string;
  /** Denormalized employee full name. */
  employeeName: string;
  /** Optional employee photo URL; null/empty -> initials avatar. */
  employeePhoto: string | null;
  /** Denormalized leave type display name. */
  leaveTypeName: string;
  /** Denormalized hex color for the leave-type badge (e.g. "#4CAF50"). */
  leaveTypeColor: string;
  /** Leave type id (used to match the type filter; backend `LeaveTypeId`). */
  leaveTypeId?: string;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  /** True when the request carries downloadable attachments. */
  hasAttachments: boolean;
  /**
   * Current real-time remaining balance for the employee + leave type (BR-4).
   * SCALAR per the backend DTO (remaining days). See `entitlementDays` for color.
   */
  currentBalance: number;
  /**
   * Total entitlement for the leave type, when the backend supplies it. Optional:
   * the current `PendingLeaveRequestDto` does NOT include it, so it is undefined
   * until the backend adds it. Drives the §8 ratio-based pill color when present.
   */
  entitlementDays?: number;
  /** When the request was submitted (queue is sorted oldest-first by this, AC-1). */
  requestedAt: string;
  /** True when the request is older than 30 days without action (BR-3). */
  isOverdue: boolean;
  /**
   * How many team members are already approved off during the overlapping
   * period (FR-5). 0 when no conflict.
   */
  teamConflictCount: number;
  /**
   * Optional downloadable attachment URLs for the detail panel (AC-4).
   * Absent in the current list response; rendered when the backend adds it.
   */
  attachmentUrls?: string[];
}

/**
 * Server-side paged result for the pending queue (FR-4, AC-2).
 * Matches the backend `PendingLeaveQueueResult` (the inner `data` of `ApiResponse<T>`).
 */
export interface IPendingLeaveResponse {
  items: IPendingLeaveRequest[];
  totalCount: number;
  page?: number;
  pageSize?: number;
}

/** Minimal shape of the backend `ApiResponse<T>` envelope the service unwraps. */
export interface IApiEnvelope<T> {
  data: T;
  success?: boolean;
  message?: string;
}

/** Sort options for the queue (FR-3). Default is oldest-requested-first (AC-1). */
export type PendingLeaveSortBy = 'requestedAt' | 'startDate';

/**
 * Query parameters for the pending queue (FR-3, FR-4).
 * All filtering/sorting/paging is server-side -- no client-side filtering (AC-3).
 */
export interface IPendingLeaveQuery {
  page: number;
  pageSize: number;
  leaveTypeId?: string | null;
  employeeId?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  sortBy?: PendingLeaveSortBy;
  /** True (default, oldest-first AC-1) for ascending; matches backend `sortAscending`. */
  sortAscending?: boolean;
}

/** Active filter chip shown above the queue (§8 chip-based active filters). */
export interface IPendingFilterChip {
  /** Human-readable category, e.g. "Type", "Employee", "From". */
  category: string;
  /** Display label for the chip value. */
  label: string;
  /** Which filter this chip maps to, for removal. */
  filterKey: 'leaveTypeId' | 'employeeId' | 'startDate' | 'endDate';
}

/** Default page size (AC-2). */
export const PENDING_DEFAULT_PAGE_SIZE = 20;

/** Page-size options; max 50 to cap data transfer (§10). */
export const PENDING_PAGE_SIZE_OPTIONS = [10, 20, 50] as const;

/** Balance pill color tiers (§8). */
export type BalanceTier = 'high' | 'medium' | 'low' | 'none';

/**
 * Pure helper: classify a remaining balance into a color tier (§8).
 *
 * When `entitlementDays` is supplied (and > 0) the ratio remaining/entitlement
 * drives the color: green > 50%, yellow 20-50%, red < 20%. A negative remaining
 * is always 'low' (red).
 *
 * When entitlement is unknown (the current backend DTO omits it), the helper
 * falls back to a sign-based tier: 'none' (neutral) for >= 0, 'low' for < 0.
 * This keeps the pill meaningful until the backend adds entitlement.
 */
export function balanceTier(
  remaining: number | null | undefined,
  entitlementDays?: number | null
): BalanceTier {
  const rem = remaining ?? 0;
  if (entitlementDays == null || entitlementDays <= 0) {
    return rem < 0 ? 'low' : 'none';
  }
  if (rem < 0) {
    return 'low';
  }
  const ratio = rem / entitlementDays;
  if (ratio > 0.5) {
    return 'high';
  }
  if (ratio >= 0.2) {
    return 'medium';
  }
  return 'low';
}

/** Tailwind pill classes per tier (Notion-style ring pills, §8). */
export const BALANCE_TIER_CLASSES: Record<BalanceTier, string> = {
  high: 'bg-green-50 text-green-700 ring-green-200',
  medium: 'bg-amber-50 text-amber-700 ring-amber-200',
  low: 'bg-red-50 text-red-700 ring-red-200',
  none: 'bg-neutral-100 text-neutral-500 ring-neutral-200',
};

/** Sort option labels for the sort dropdown (FR-3). */
export const PENDING_SORT_OPTIONS: { value: PendingLeaveSortBy; label: string }[] = [
  { value: 'requestedAt', label: 'Requested date (oldest first)' },
  { value: 'startDate', label: 'Start date (earliest first)' },
];

/**
 * US-LV-005: Approve / Reject action contract (backend building in parallel — RECONCILE).
 *
 *   POST /api/v1/leaves/{id}/approve  body { comment? }  -> ILeaveActionResult
 *   POST /api/v1/leaves/{id}/reject   body { reason }    -> ILeaveActionResult  (reason required, BR-2)
 *
 * Error mapping (surfaced via toast / modal in the component):
 *   - 409 Conflict          -> request already actioned by another manager (AC-5). Refresh queue.
 *   - 400 Bad Request with `code: 'insufficient_balance'` and `negativeBalanceAllowed: true`
 *                           -> blocked pending confirmation; FE shows the confirm modal and
 *                              retries the approve with `confirmNegativeBalance: true` (AC-3).
 *   - 400 Bad Request with `code: 'insufficient_balance'` and `negativeBalanceAllowed: false`
 *                           -> hard block; FE surfaces `message` via toast (AC-3).
 *   - 400 Bad Request with `code: 'payroll_locked'` -> surface `message` via toast (BR-4, deferred).
 *   - Any other error       -> surface `message` (or a generic fallback) via toast.
 */

/** Body for the approve action (AC-1). Comment is optional (BR-2). */
export interface IApproveLeaveRequest {
  /** Optional manager comment. */
  comment?: string;
  /**
   * Set true on the second attempt after the insufficient-balance confirmation
   * modal, telling the backend the manager accepts going negative (AC-3).
   */
  confirmNegativeBalance?: boolean;
}

/** Body for the reject action (AC-2, BR-2). Reason is mandatory. */
export interface IRejectLeaveRequest {
  /** Mandatory rejection reason. */
  reason: string;
}

/**
 * Result of an approve/reject action. The backend returns the updated request
 * status; the FE uses the status to surface the right confirmation and, for
 * multi-level (AC-4, DEFERRED), to show a "Pending L2 Approval"-style status
 * rather than treating it as fully approved.
 */
export interface ILeaveActionResult {
  requestId: string;
  /** New status, e.g. "Approved", "Rejected", or "Pending L2 Approval" (AC-4). */
  status: string;
  /** Updated remaining balance after approval, when the backend supplies it. */
  currentBalance?: number;
}

/**
 * Typed error body for approve/reject failures (AC-3, AC-5, BR-4).
 * `code` is the machine-readable discriminator; `message` is shown verbatim.
 */
export interface ILeaveActionErrorResponse {
  message: string;
  code?:
    | 'insufficient_balance'
    | 'payroll_locked'
    | 'already_actioned'
    | string;
  /**
   * Only meaningful with `code: 'insufficient_balance'`. True when the leave
   * type permits a negative balance — the FE then asks for confirmation and
   * retries; false/absent means a hard block (AC-3).
   */
  negativeBalanceAllowed?: boolean;
}

/**
 * AC-4 (DEFERRED): statuses that indicate a request moved to a further approval
 * level rather than being finalized. When the backend returns one of these the
 * FE shows it as a status badge and still removes the item from THIS manager's
 * queue (it is no longer pending at this level). TODO(US-ADM-007): level routing.
 */
export function isFurtherApprovalStatus(status: string | null | undefined): boolean {
  if (!status) {
    return false;
  }
  return /pending\s*l?\d|pending\s*level|next\s*approval/i.test(status);
}
