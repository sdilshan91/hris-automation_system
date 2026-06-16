/**
 * US-PAY-003: Models for payroll RUNS — listing runs, the pre-run validation
 * summary, initiating a run, and the live progress / completion summary.
 *
 * Backend endpoints (assumed REST contract — backend agent building in parallel;
 * the service layer is intentionally thin so a route mismatch is a one-file fix).
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   GET    /payroll/runs                         - list runs (table view, §8)
 *   GET    /payroll/runs/:id                     - get one run (detail + summary)
 *   GET    /payroll/runs/:id/progress            - poll processing progress (FR-6)
 *   POST   /payroll/runs/validate                - pre-run validation summary (§8)
 *   POST   /payroll/runs                         - initiate a run (AC-1, 202 Accepted)
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend stamps
 * tenant_id + audit fields and enforces RLS (AC-7) — the FE never sends them.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads.
 *
 * ENUM CASING (US-PLT-003 — critical): `PayrollRunStatus` is a PascalCase string
 * union matching the C# member names (global JsonStringEnumConverter); statuses
 * arrive as STRINGS, never integers.
 */

// ─── Status enum (FR-1, BR-6) ──────────────────────────────────

/**
 * Payroll run lifecycle status (§7, BR-6). Matches C# `PayrollRunStatus`.
 * Approval transitions (US-PAY-008 BR-4):
 *   Queued -> Processing -> ReviewPending -> AwaitingApproval -> Approved -> Finalized
 *   AwaitingApproval -> Rejected -> ReviewPending (after corrections)
 *   any pre-Finalized status -> Cancelled.
 *
 * AwaitingApproval + Rejected are added by US-PAY-008. Rejected is an OFF-PATH
 * state (like Cancelled) — it is not a stepper node; the run returns to
 * ReviewPending once HR corrects + re-submits (BR-3).
 */
export type PayrollRunStatus =
  | 'Queued'
  | 'Processing'
  | 'ReviewPending'
  | 'AwaitingApproval'
  | 'Approved'
  | 'Finalized'
  | 'Rejected'
  | 'Cancelled';

/** Tailwind badge classes per status (§8 color-coded badge). Single source of truth. */
export const RUN_STATUS_BADGE: Record<PayrollRunStatus, string> = {
  Queued: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Processing: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  ReviewPending: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  AwaitingApproval: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Approved: 'bg-violet-50 text-violet-700 ring-violet-600/20',
  Finalized: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Rejected: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Cancelled: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** Human-readable status labels (wire value is PascalCase, BR-6). */
export const RUN_STATUS_LABELS: Record<PayrollRunStatus, string> = {
  Queued: 'Queued',
  Processing: 'Processing',
  ReviewPending: 'Review pending',
  AwaitingApproval: 'Awaiting approval',
  Approved: 'Approved',
  Finalized: 'Finalized',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
};

/**
 * The horizontal status stepper steps (§8): Review > Awaiting approval >
 * Approved > Finalized. Queued/Processing precede these. Cancelled and Rejected
 * are terminal/off-path states shown separately, not as stepper nodes (BR-4).
 */
export const RUN_STEPPER: { status: PayrollRunStatus; label: string }[] = [
  { status: 'Queued', label: 'Queued' },
  { status: 'Processing', label: 'Processing' },
  { status: 'ReviewPending', label: 'Review' },
  { status: 'AwaitingApproval', label: 'Awaiting approval' },
  { status: 'Approved', label: 'Approved' },
  { status: 'Finalized', label: 'Finalized' },
];

/** Month dropdown options (1-12) for the New Run modal (§8). */
export const PAY_MONTHS: { value: number; label: string }[] = [
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

// ─── Run summary (list + detail) ───────────────────────────────

/**
 * A payroll run as shown in the Notion-style table (§8) and detail view. Carries
 * the period, status, employee counts, and money totals (§7 payroll_run table).
 * Totals are 0 until the run reaches ReviewPending.
 */
export interface IPayrollRun {
  id: string;
  payMonth: number;
  payYear: number;
  status: PayrollRunStatus;
  totalEmployees: number;
  processedEmployees: number;
  skippedEmployees: number;
  totalGross: number;
  totalDeductions: number;
  totalNet: number;
  /** Denormalized display name of the initiator (FK initiated_by, §7). */
  initiatedByName: string | null;
  /** ISO timestamp (initiated_at, §7). */
  initiatedAt: string;
  /** ISO timestamp, null until the run finishes (completed_at, §7). */
  completedAt: string | null;
}

// ─── Pre-run validation summary (§8) ───────────────────────────

/** Request to validate a period before initiating a run (§8 pre-run summary). */
export interface IPayrollRunValidationRequest {
  payMonth: number;
  payYear: number;
}

/**
 * Pre-run validation summary (§8: "247 employees ready, 3 missing salary
 * structure"). `canRun` gates the modal's Submit; `blockers` carry hard stops
 * (e.g. period already finalized — AC-4 — or attendance not locked — BR-3).
 */
export interface IPayrollRunValidation {
  /** Total active employees in the tenant for the period. */
  totalEmployees: number;
  /** Employees with a salary structure assigned — eligible for the run. */
  readyEmployees: number;
  /** Employees skipped for lacking a salary structure (AC-6, FR-5). */
  missingSalaryStructure: number;
  /** False when a hard blocker exists; the modal disables Submit (AC-4, BR-3). */
  canRun: boolean;
  /** Human-readable hard-stop reasons shown in the modal when `canRun` is false. */
  blockers: string[];
}

// ─── Initiate a run (AC-1, FR-9) ───────────────────────────────

/** Body to initiate a payroll run (AC-1). The Idempotency-Key goes in a header (FR-9). */
export interface IInitiatePayrollRunRequest {
  payMonth: number;
  payYear: number;
}

// ─── Live progress (FR-6) ──────────────────────────────────────

/**
 * Processing progress for the in-progress view (FR-6, §8: "Processing 1,247 /
 * 5,000 employees..."). The FE polls this every ~2s while the run is Processing.
 *
 * SignalR hook: the backend MAY instead push these same fields over a SignalR
 * hub. `PayrollRunService.streamProgress` is the single isolated swap point — see
 * its comment. The shape is identical so the UI is agnostic to the transport.
 */
export interface IPayrollRunProgress {
  runId: string;
  status: PayrollRunStatus;
  processedEmployees: number;
  totalEmployees: number;
  skippedEmployees: number;
}
