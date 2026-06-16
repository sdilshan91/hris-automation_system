/**
 * US-PAY-010: Models for attendance/leave → payroll integration — the pre-payroll
 * reconciliation report (FR-7) and the manual leave-encashment trigger (AC-3/FR-5).
 *
 * Backend contract (PINNED — `apiBaseUrl` already includes `/api/v1`, so the resource
 * is `${apiBaseUrl}/payroll/...`):
 *   GET  /payroll/reconciliation?payYear=&payMonth=  - per-employee attendance/leave
 *                                                      summary + finalized flag (FR-7)
 *   POST /payroll/leave-encashments                  - trigger encashment (AC-3/FR-5)
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header)
 * and withCredentials for the httpOnly cookie auth. The backend stamps tenant_id and
 * enforces RLS (AC-5/FR-8) — the FE never sends tenant info.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads.
 */

// ─── Reconciliation report (FR-7) ──────────────────────────────

/**
 * One employee's pre-payroll reconciliation row (FR-7) — the attendance + leave inputs
 * HR reviews before initiating a run. Field names mirror the backend
 * `PrePayrollReconciliationRowDto`. `leaveDaysByType` is a map of leave-type name →
 * approved days; `totalLeaveDays` is the sum. `calculatedLopDays` already incorporates
 * the late-deduction + half-day rules (BR-2/BR-3) — the FE only displays it.
 */
export interface IReconciliationRow {
  employeeId: string;
  employeeNo: string;
  employeeName: string;
  /** Scheduled working days in the period (shift calendar). */
  workingDays: number;
  /** Present days including half-days. */
  presentDays: number;
  /** Absent days. */
  absentDays: number;
  /** Approved-leave days in the period, broken down by leave-type name. */
  leaveDaysByType: Record<string, number>;
  /** Total approved-leave days across all types. */
  totalLeaveDays: number;
  /** Approved overtime hours (approved minutes / 60). */
  overtimeHours: number;
  /** Calculated loss-of-pay days (already incorporates late/half-day, BR-2/BR-3). */
  calculatedLopDays: number;
}

/**
 * The pre-payroll reconciliation report for a period (FR-7), tenant-scoped. Mirrors the
 * backend `PrePayrollReconciliationDto`. `attendanceFinalized` is the AC-4 banner driver:
 * the payroll-initiation flow blocks Initiate when this is false.
 */
export interface IReconciliationReport {
  /** The period, "yyyy-MM". */
  period: string;
  payMonth: number;
  payYear: number;
  /** True when the attendance period is locked/finalized (AC-4 banner driver). */
  attendanceFinalized: boolean;
  rows: IReconciliationRow[];
}

// ─── Leave encashment trigger (AC-3/FR-5) ──────────────────────

/**
 * Request body to trigger a leave encashment (AC-3/FR-5). Mirrors the backend
 * `LeaveEncashmentRequest`. `leaveTypeId` is optional — null means an HR override with a
 * manual day count. The encashment is added as an earning adjustment to the next
 * available run for the target period.
 */
export interface ILeaveEncashmentRequest {
  employeeId: string;
  /** Null = HR override with a manual day count (no leave-type eligibility check). */
  leaveTypeId?: string | null;
  eligibleDays: number;
  /** Target pay-period month (1-12); the encashment lands in the next available run. */
  payMonth: number;
  payYear: number;
  /** Whether the encashment is taxable (default true). */
  isTaxable: boolean;
}

/**
 * The result of a leave encashment (AC-3/FR-5). Mirrors the backend
 * `LeaveEncashmentResultDto`. `periodDeferred` is true when the requested period's run
 * was already Finalized/Processing and the encashment was pushed to a later period.
 */
export interface ILeaveEncashmentResult {
  adjustmentId: string;
  employeeId: string;
  /** Days actually applied after any BR-6 cap. */
  encashedDays: number;
  /** The daily rate used = monthly_basic / working_days. */
  dailyRate: number;
  /** The encashment amount = encashedDays * dailyRate. */
  amount: number;
  /** The pay-period month the adjustment was created for (after BR-7/BR-8 deferral). */
  payMonth: number;
  payYear: number;
  /** True when the target period was deferred to a later run. */
  periodDeferred: boolean;
}

// ─── View helpers (pure, unit-tested) ──────────────────────────

/**
 * The visual reconciliation state of an employee's attendance for the period, driving
 * the color-coded cells / row badge (§8):
 *  - `mismatch`: absent days exceed the approved leave that covers them — the headline
 *    case the "Reconcile" action surfaces (absence with no leave record).
 *  - `lop`: calculated LOP days > 0 (loss of pay will be deducted) but no unexplained gap.
 *  - `absence`: some absence, but fully covered by approved leave (no LOP).
 *  - `full`: present for every working day, no absence (green).
 */
export type ReconciliationState = 'full' | 'absence' | 'lop' | 'mismatch';

/**
 * Classify a reconciliation row (§8 color-coding + the "Reconcile" mismatch surface).
 *
 * A MISMATCH is an absence not covered by approved leave — i.e. uncovered absence
 * (absentDays − totalLeaveDays) is positive AND there is calculated LOP. This is the
 * "absent day with no leave record" the §8 Reconcile button highlights. A row with LOP
 * but where leave fully covers the absence is just `lop`; absence fully covered by leave
 * with no LOP is `absence`; a clean full-attendance row is `full`.
 */
export function reconciliationState(row: IReconciliationRow): ReconciliationState {
  const uncoveredAbsence = round2(row.absentDays - row.totalLeaveDays);
  if (uncoveredAbsence > 0 && row.calculatedLopDays > 0) {
    return 'mismatch';
  }
  if (row.calculatedLopDays > 0) {
    return 'lop';
  }
  if (row.absentDays > 0) {
    return 'absence';
  }
  return 'full';
}

/** True when a row has an absence that approved leave does not fully cover (§8 Reconcile). */
export function hasMismatch(row: IReconciliationRow): boolean {
  return reconciliationState(row) === 'mismatch';
}

/** Tailwind classes for the per-row state badge (§8). Single source of truth. */
export const RECON_STATE_BADGE: Record<ReconciliationState, string> = {
  full: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  absence: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  lop: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  mismatch: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** Human-readable per-row state labels (§8). */
export const RECON_STATE_LABELS: Record<ReconciliationState, string> = {
  full: 'Full attendance',
  absence: 'On leave',
  lop: 'Has LOP',
  mismatch: 'Unreconciled',
};

/**
 * Flatten a row's `leaveDaysByType` map into a stable, name-sorted list for display
 * (the leave-days tooltip / cell). Zero-day entries are dropped.
 */
export function leaveBreakdown(
  row: IReconciliationRow,
): { type: string; days: number }[] {
  return Object.entries(row.leaveDaysByType ?? {})
    .filter(([, days]) => days > 0)
    .map(([type, days]) => ({ type, days }))
    .sort((a, b) => a.type.localeCompare(b.type));
}

/** "yyyy-MM" period for the reconciliation query, zero-padded month. */
export function reconciliationPeriod(month: number, year: number): string {
  return `${year}-${`${month}`.padStart(2, '0')}`;
}

/** Round to 2 decimals (day fractions: half-days etc.) without float noise. */
function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

/** Month dropdown options (1-12) for the period selector (§8). */
export const RECON_PAY_MONTHS: { value: number; label: string }[] = [
  { value: 1, label: 'January' },
  { value: 2, label: 'February' },
  { value: 3, label: 'March' },
  { value: 4, label: 'April' },
  { value: 5, label: 'May' },
  { value: 6, label: 'June' },
  { value: 7, label: 'July' },
  { value: 8, label: 'August' },
  { value: 9, label: 'September' },
  { value: 10, label: 'October' },
  { value: 11, label: 'November' },
  { value: 12, label: 'December' },
];
