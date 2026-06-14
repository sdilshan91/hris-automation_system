/**
 * US-LV-006: Leave Balance Dashboard models matching the backend API contract.
 *
 * Backend endpoints (backend agent building in parallel -- assumed contract,
 * RECONCILE against docs/vault/modules/leave-management.md "Frontend (US-LV-006)"):
 *   GET /api/v1/leaves/my-balance?year={year}             -> ILeaveBalanceSummary[]  (FR-1, FR-2)
 *   GET /api/v1/leaves/my-ledger?leaveTypeId={id}&year={year} -> ILeaveLedgerEntry[] (FR-3)
 *   GET /api/v1/leaves/my-upcoming                        -> ILeaveRequest[]         (FR-4)
 *
 * Past-request history (FR-6) reuses the existing GET /api/v1/leaves/mine endpoint
 * from US-LV-003 (LeaveRequestService.getMyLeaveRequests) -- no new call is added.
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the leaves resource is `${apiBaseUrl}/leaves`.
 */

/**
 * Per-leave-type balance summary card data (FR-2, AC-1).
 * BR-1: balance = entitlement + carryForward - used - expired + adjustments.
 * BR-2: `pending` days are shown separately and NOT deducted from `balance`.
 */
export interface ILeaveBalanceSummary {
  leaveTypeId: string;
  leaveTypeName: string;
  /** Hex accent color (e.g. "#2563eb"). Used as a visual accent, never the sole signal (NFR-4). */
  color: string;
  /** Total entitlement (including carry-forward already folded in by the backend, see BR-1). */
  entitlement: number;
  used: number;
  /** Approved-but-future + pending days shown separately (BR-2). */
  pending: number;
  /** Remaining balance per BR-1. May be negative if the type allows it. */
  balance: number;
  carryForward: number;
  expired: number;
  /** BR-3: deactivated leave types with a remaining balance are shown collapsed/archived. */
  isArchived?: boolean;
  /**
   * US-LV-008 (§8): the expiry date of the currently carried-forward days,
   * date-only ('YYYY-MM-DD'). OPTIONAL — the dashboard shows the amber
   * "expiring on <date>" indicator only when the backend supplies this AND
   * `carryForward > 0`. If absent, the carry-forward line still renders without
   * a date.
   *
   * TODO(carry-forward-expiry-date): the US-LV-006 my-balance endpoint does not
   * yet return this. The backend `ProcessLeaveYearEndJob` records a
   * `carry_forward` ledger entry with `transaction_date = first day of new
   * leave year` and a `carry_forward_expiry_months` window (US-LV-008 FR-1/BR-3);
   * the my-balance projection should surface the resulting expiry date here so
   * the amber indicator can light up. Until then this field is undefined.
   */
  carryForwardExpiry?: string | null;
}

/** Ledger transaction kind (FR-3). Mirrors the backend LeaveLedger entry_type enum. */
export type LedgerEntryType =
  | 'Accrual'
  | 'Used'
  | 'Adjusted'
  | 'Encashed'
  | 'CarryForward'
  | 'Expired';

/**
 * A single immutable ledger transaction for the detail view (AC-2, FR-3).
 * Ordered by `occurredAt` ascending by the backend.
 */
export interface ILeaveLedgerEntry {
  ledgerId: string;
  leaveTypeId: string;
  leaveYear: number;
  entryType: LedgerEntryType;
  /** Signed change in days (+ accrual/carry-forward, - used/expired, +/- adjustment). */
  amount: number;
  /** Running balance after this entry was applied. */
  balanceAfter: number;
  description: string;
  occurredAt: string;
}

/**
 * The set of years offered in the year-selector pill group (BR-5).
 * Computed client-side around the current year so the employee can view prior years read-only.
 */
export function buildYearOptions(currentYear: number, lookback = 2): number[] {
  const years: number[] = [];
  for (let y = currentYear - lookback; y <= currentYear; y++) {
    years.push(y);
  }
  return years;
}

/**
 * Pure helper: recompute the balance from its parts (BR-1) for display verification.
 * The backend remains authoritative for `balance`; this lets the UI cross-check and
 * lets tests assert the math without hitting the API.
 *
 *   balance = entitlement + carryForward - used - expired + adjustments
 *
 * `adjustments` is not a separate summary field; the backend folds it into `balance`.
 * When `adjustments` is omitted we derive it so the identity holds for the given summary.
 */
export function computeBalance(
  s: Pick<ILeaveBalanceSummary, 'entitlement' | 'carryForward' | 'used' | 'expired'>,
  adjustments = 0,
): number {
  return s.entitlement + s.carryForward - s.used - s.expired + adjustments;
}

/**
 * Pure helper: the fraction of entitlement consumed (used only, pending excluded per BR-2),
 * clamped to [0, 1] for the arc/progress indicator. Returns 0 when entitlement is 0
 * (avoids divide-by-zero for unpaid/zero-entitlement types).
 */
export function usedFraction(s: Pick<ILeaveBalanceSummary, 'entitlement' | 'used'>): number {
  if (!s.entitlement || s.entitlement <= 0) {
    return 0;
  }
  const f = s.used / s.entitlement;
  if (f < 0) return 0;
  if (f > 1) return 1;
  return f;
}

/** Badge styling tokens per ledger entry type (§8: green accrual, red used, blue adjustment). */
export const LEDGER_BADGE_CLASSES: Record<LedgerEntryType, string> = {
  Accrual: 'bg-green-50 text-green-700 ring-green-200',
  CarryForward: 'bg-green-50 text-green-700 ring-green-200',
  Used: 'bg-red-50 text-red-700 ring-red-200',
  Expired: 'bg-red-50 text-red-700 ring-red-200',
  Adjusted: 'bg-blue-50 text-blue-700 ring-blue-200',
  Encashed: 'bg-blue-50 text-blue-700 ring-blue-200',
};

/** Human-readable label for a ledger entry type. */
export const LEDGER_ENTRY_LABELS: Record<LedgerEntryType, string> = {
  Accrual: 'Accrual',
  Used: 'Used',
  Adjusted: 'Adjustment',
  Encashed: 'Encashed',
  CarryForward: 'Carry-forward',
  Expired: 'Expired',
};
