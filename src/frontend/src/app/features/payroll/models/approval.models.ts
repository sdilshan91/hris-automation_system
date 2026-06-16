/**
 * US-PAY-008: Models for the payroll APPROVAL workflow — the approval review
 * summary (totals + month-over-month variance + exceptions), the approval-history
 * timeline, and the action requests (submit / approve / reject / return / finalize).
 *
 * Backend contract is the ASSUMED REST shape from the story brief — the backend
 * agent was building in parallel and had NOT pinned routes/strings in the vault
 * when this was written. Route strings live ONLY in `payroll-approval.service.ts`,
 * so any mismatch is a one-file fix.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so services consume BARE payloads.
 *
 * ENUM CASING (US-PLT-003 — critical): `ApprovalAction` is a PascalCase string
 * union matching the C# member names (global JsonStringEnumConverter); actions
 * arrive as STRINGS.
 */

// ─── Approval action (§7 payroll_approval_history.action) ──────

/** An action recorded in the approval audit trail (§7, FR-7). PascalCase wire. */
export type ApprovalAction =
  | 'Submitted'
  | 'Approved'
  | 'Rejected'
  | 'Returned'
  | 'Escalated';

/** Tailwind badge classes per action for the history timeline (§8). */
export const APPROVAL_ACTION_BADGE: Record<ApprovalAction, string> = {
  Submitted: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Approved: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Rejected: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Returned: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  Escalated: 'bg-violet-50 text-violet-700 ring-violet-600/20',
};

/** Human-readable labels for the approval action (wire value is PascalCase). */
export const APPROVAL_ACTION_LABELS: Record<ApprovalAction, string> = {
  Submitted: 'Submitted for approval',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Returned: 'Returned to HR',
  Escalated: 'Escalated',
};

// ─── Approval review summary (FR-4, §7) ────────────────────────

/** Severity of an exception/warning shown on the approval review page (FR-4). */
export type ExceptionSeverity = 'Warning' | 'Error';

/**
 * A single exception/warning surfaced for the approver (FR-4: missing structures,
 * negative net, etc.). `employeeName` is optional context when the exception is
 * employee-scoped.
 */
export interface IPayrollException {
  severity: ExceptionSeverity;
  message: string;
  employeeName?: string | null;
}

/**
 * The comprehensive payroll summary an approver reviews (FR-4, §7). Carries the
 * current-run totals plus the previous month's net for variance, and the list of
 * exceptions. `variancePercentage` is month-over-month change on total net (may be
 * null when there is no previous month to compare against).
 */
export interface IApprovalSummary {
  runId: string;
  totalEmployees: number;
  totalGross: number;
  totalDeductions: number;
  /** Statutory subset of deductions (§7 total_statutory). */
  totalStatutory: number;
  totalNet: number;
  /** Previous month's total net for the variance comparison (null if none). */
  previousMonthTotalNet: number | null;
  /** Month-over-month % change on net (server-provided; null if no baseline). */
  variancePercentage: number | null;
  exceptions: IPayrollException[];
}

// ─── Variance band (§8 color-coded comparison) ─────────────────

/**
 * Variance band for the comparison view color-coding (§8):
 *  - 'decrease' (green): net went down or is flat — no concern.
 *  - 'normal'   (neutral): increase up to 5%.
 *  - 'elevated' (amber): increase > 5% — worth a look.
 *  - 'high'     (red): increase > 15% — flag prominently.
 */
export type VarianceBand = 'decrease' | 'normal' | 'elevated' | 'high';

/** Tailwind classes (text colour) per variance band for the §8 highlight. */
export const VARIANCE_BAND_CLASS: Record<VarianceBand, string> = {
  decrease: 'text-emerald-700',
  normal: 'text-neutral-700',
  elevated: 'text-amber-700',
  high: 'text-rose-700',
};

/**
 * PURE classifier for the §8 month-over-month variance highlight. Isolated from the
 * component (mirrors the BE side-effect-free calc) so it is trivially unit-testable.
 *
 * A null variance (no previous month) is treated as 'normal' (nothing to flag).
 * Thresholds (BR/§8): decrease/flat = green; >0..5% = neutral; >5..15% = amber;
 * >15% = red. Negative (a decrease) is always green regardless of magnitude.
 */
export function varianceBand(variancePercentage: number | null): VarianceBand {
  if (variancePercentage === null || variancePercentage === undefined) {
    return 'normal';
  }
  if (variancePercentage <= 0) {
    return 'decrease';
  }
  if (variancePercentage <= 5) {
    return 'normal';
  }
  if (variancePercentage <= 15) {
    return 'elevated';
  }
  return 'high';
}

// ─── Approval history timeline (§7, FR-7) ──────────────────────

/**
 * One entry in the approval audit trail / timeline (§7 payroll_approval_history,
 * FR-7). Newest-first when rendered. `ipAddress` is captured server-side (FR-7).
 */
export interface IApprovalHistoryEntry {
  id: string;
  stepNumber: number;
  action: ApprovalAction;
  /** Denormalized display name of the actor (FK actor_user_id, §7). */
  actorName: string | null;
  comments: string | null;
  /** ISO timestamp (acted_at, §7). */
  actedAt: string;
  ipAddress: string | null;
}

// ─── Action requests (AC-1/2/3, FR-9) ──────────────────────────

/**
 * Comments payload for reject (AC-3, reason REQUIRED) and return (FR-9, comments
 * REQUIRED). Submit/approve/finalize carry no body.
 */
export interface IApprovalCommentRequest {
  comments: string;
}
