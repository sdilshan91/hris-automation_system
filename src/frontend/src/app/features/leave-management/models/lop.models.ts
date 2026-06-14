/**
 * US-LV-011: Loss of Pay (LOP) / Compulsory Leave management models.
 *
 * The HR LOP management screen lists LOP `leave_request` entries (extended with
 * `is_lop = true`, `lop_source`) and drives three HR actions:
 *   - Bulk LOP assignment      → POST /api/v1/leaves/assign-lop          (FR-3)
 *   - Compulsory leave assign  → POST /api/v1/leaves/compulsory          (FR-6)
 *   - Override a system LOP     → POST /api/v1/leaves/lop/{id}/override   (BR-3)
 * plus a read for display:     GET  /api/v1/leaves/lop-summary           (FR-5)
 *
 * The backend is the source of truth for balance-vs-LOP decisions, payroll-lock
 * immutability (NFR-3/BR-5), and notifications (BR-6); the FE surfaces results.
 *
 * `apiBaseUrl` already includes `/api/v1`, so the resource base is `${apiBaseUrl}/leaves`.
 */

/**
 * Origin of an LOP entry (FR-4, §7 `lop_source`). Drives the filter chips and the
 * red/orange row highlight, and gates the BR-3 override action (only
 * `system_generated` entries can be converted).
 */
export type LopSource =
  | 'employee_request'
  | 'system_generated'
  | 'hr_assigned'
  | 'compulsory';

/** Filter selection for the LOP list — the four sources plus "all". */
export type LopSourceFilter = LopSource | 'all';

/**
 * A single LOP entry as shown in the HR management list (FR-5 lop-summary row).
 *
 * Denormalized `employeeName` / `leaveTypeName` so the list needs no extra lookup.
 * `date` is a date-only string (`YYYY-MM-DD`); the FE slices it for display (never
 * `new Date()`-parses) to avoid TZ off-by-one, consistent with US-LV-007/009.
 */
export interface ILopEntry {
  /** The underlying leave_request id. */
  leaveRequestId: string;
  employeeId: string;
  /** Denormalized for display. */
  employeeName: string;
  /** Denormalized employee number, when available. */
  employeeNo?: string | null;
  /** The day this LOP entry applies to (date-only 'YYYY-MM-DD'). */
  date: string;
  /** LOP day count for this entry (usually 1; halved for half-days). */
  days: number;
  /** Origin of the entry (FR-4) — drives the highlight + override gating. */
  source: LopSource;
  /** Backend status label, e.g. "System-Generated" / "HR-Assigned". */
  status: string;
  /** Reason / note captured at assignment. */
  reason?: string | null;
  /** Denormalized leave type name (LOP types are renameable, FR-1). */
  leaveTypeName?: string | null;
  /**
   * True when the entry's payroll period is finalized (NFR-3/BR-5) — immutable,
   * so the override action is disabled. Optional; absent ⇒ not locked.
   */
  payrollLocked?: boolean;
}

/** Bulk LOP assignment payload (FR-3): one employee, a set of explicit dates. */
export interface IAssignLopRequest {
  employeeId: string;
  /** Explicit dates ('YYYY-MM-DD') to mark as LOP. */
  dates: string[];
  reason: string;
}

/** Result of a bulk LOP assignment (per-day rows created). */
export interface IAssignLopResult {
  employeeId: string;
  created: number;
  /** Dates that were skipped (e.g. already LOP / payroll-locked), if any. */
  skipped?: string[];
}

/**
 * Compulsory leave (company shutdown) bulk assignment (FR-6, BR-4).
 *
 * Deducts from each employee's relevant leave balance first; if insufficient it
 * becomes LOP (BR-4) — the backend decides per employee. `applyToAll` assigns to
 * every active employee; otherwise `employeeIds` scopes it.
 */
export interface IAssignCompulsoryLeaveRequest {
  /** Dates ('YYYY-MM-DD') of the compulsory leave / shutdown. */
  dates: string[];
  /** Leave type deducted first (BR-4). */
  leaveTypeId: string;
  reason: string;
  /** When true, applies to all active employees (ignores `employeeIds`). */
  applyToAll: boolean;
  /** Explicit employee scope when `applyToAll` is false. */
  employeeIds?: string[];
}

/** Result of a compulsory-leave assignment (FR-6, BR-4). */
export interface IAssignCompulsoryLeaveResult {
  /** Employees whose balance covered it (deducted, not LOP). */
  deducted: number;
  /** Employees with insufficient balance → became LOP (BR-4). */
  lop: number;
  total: number;
}

/**
 * Override a system-generated LOP entry (BR-3): convert it to a different leave
 * type (e.g. the employee later provided a valid reason). The backend deducts
 * from the target type's balance and removes the LOP flag.
 */
export interface IOverrideLopRequest {
  /** Target leave type to convert the LOP entry into. */
  leaveTypeId: string;
  reason: string;
}

/** Typed error body for LOP operations (surfaced verbatim via toast). */
export interface ILopErrorResponse {
  message: string;
  /** e.g. 'payroll_locked' (BR-5), 'not_overridable' (BR-3), 'insufficient_balance'. */
  code?: string;
}

// ─── Display helpers (pure, unit-tested) ──────────────────────

/** Human-readable label for each LOP source (filter chips + row meta). */
export const LOP_SOURCE_LABELS: Record<LopSource, string> = {
  employee_request: 'Employee-requested',
  system_generated: 'Auto-generated',
  hr_assigned: 'HR-assigned',
  compulsory: 'Compulsory',
};

/** Filter chip options, in display order, including the "all" pill. */
export const LOP_SOURCE_FILTERS: { value: LopSourceFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'system_generated', label: 'Auto-generated' },
  { value: 'hr_assigned', label: 'HR-assigned' },
  { value: 'employee_request', label: 'Employee-requested' },
  { value: 'compulsory', label: 'Compulsory' },
];

/** Display label for a source value. */
export function lopSourceLabel(source: LopSource): string {
  return LOP_SOURCE_LABELS[source] ?? source;
}

/**
 * Row highlight classes (§8: "highlighted in red/orange").
 * Auto-generated (system) LOP = red emphasis; all other LOP rows = orange/amber.
 * Color is never the sole indicator — the source label is always shown too (NFR a11y).
 */
export function lopRowClasses(source: LopSource): string {
  return source === 'system_generated'
    ? 'bg-red-50/60 border-l-4 border-red-400'
    : 'bg-orange-50/60 border-l-4 border-orange-400';
}

/** Badge classes for the source pill (matches the row accent). */
export function lopSourceBadgeClasses(source: LopSource): string {
  return source === 'system_generated'
    ? 'bg-red-100 text-red-700 ring-red-200'
    : 'bg-orange-100 text-orange-700 ring-orange-200';
}

/**
 * BR-3: only system-generated LOP entries can be overridden (converted), and only
 * when the payroll period is not locked (BR-5/NFR-3).
 */
export function canOverrideLop(entry: Pick<ILopEntry, 'source' | 'payrollLocked'>): boolean {
  return entry.source === 'system_generated' && entry.payrollLocked !== true;
}

/** Filter a list of entries by the selected source ('all' returns everything). */
export function filterLopEntries(entries: ILopEntry[], filter: LopSourceFilter): ILopEntry[] {
  if (filter === 'all') {
    return entries;
  }
  return entries.filter((e) => e.source === filter);
}

/**
 * Expand an inclusive date range into an array of date-only strings ('YYYY-MM-DD').
 * Returns [] for an invalid range (start after end / unparseable). Used by the
 * bulk-assign + compulsory forms which collect a from/to range but submit `dates[]`.
 */
export function expandDateRange(from: string, to: string): string[] {
  if (!from || !to) {
    return [];
  }
  const start = new Date(from + 'T00:00:00');
  const end = new Date(to + 'T00:00:00');
  if (isNaN(start.getTime()) || isNaN(end.getTime()) || start > end) {
    return [];
  }
  const out: string[] = [];
  const cursor = new Date(start);
  while (cursor <= end) {
    const y = cursor.getFullYear();
    const m = String(cursor.getMonth() + 1).padStart(2, '0');
    const d = String(cursor.getDate()).padStart(2, '0');
    out.push(`${y}-${m}-${d}`);
    cursor.setDate(cursor.getDate() + 1);
  }
  return out;
}
