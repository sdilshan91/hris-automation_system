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

import type { Schema } from '@core/api';

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

/**
 * The run states in which payslip PDFs may be generated or regenerated (US-PAY-004 BR-1).
 *
 * **Mirrors the server exactly** — `PayslipGenerationService.GenerateAsync` rejects anything else with
 * 400 `run_not_ready_for_payslips`. It is listed here once because the UI previously carried its own
 * version of the rule (`status !== 'Finalized'`), which was wrong in BOTH directions: it enabled the
 * button on a Draft/Queued run where the call always 400s, and disabled it on a Finalized run where the
 * backend explicitly allows regeneration after a template change. The component's own comments disagreed
 * with each other about which was intended.
 */
export const PAYSLIP_GENERATION_STATUSES: readonly PayrollRunStatus[] = [
  'ReviewPending',
  'Approved',
  'Finalized',
] as const;

/** Whether payslip generation would be accepted for a run in this state (BR-1). */
export function canGeneratePayslipsFor(
  status: PayrollRunStatus | null | undefined,
): boolean {
  return status != null && PAYSLIP_GENERATION_STATUSES.includes(status);
}

/**
 * Whether generating would OVERWRITE payslips that may already have been distributed. Regeneration on a
 * finalized run is supported by the backend and is the after-a-template-change use case, but the PDFs may
 * already be in employees' inboxes — so the UI confirms rather than doing it on one click.
 */
export function payslipRegenerationNeedsConfirmation(
  status: PayrollRunStatus | null | undefined,
): boolean {
  return status === 'Finalized';
}

/**
 * Tailwind badge classes per status (§8 color-coded badge). Single source of truth.
 *
 * ISSUE-317 / DF-12: the backend tolerates a corrupt enum row by returning the
 * `Unknown` sentinel (see the tolerant enum-read converter). The `Unknown` entry
 * gives a corrupt row a visible amber badge instead of rendering blank. The key is
 * widened with `| 'Unknown'` without polluting the `PayrollRunStatus` union.
 */
export const RUN_STATUS_BADGE: Record<PayrollRunStatus | 'Unknown', string> = {
  Queued: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Processing: 'bg-blue-50 text-blue-700 ring-blue-600/20',
  ReviewPending: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  AwaitingApproval: 'bg-sky-50 text-sky-700 ring-sky-600/20',
  Approved: 'bg-violet-50 text-violet-700 ring-violet-600/20',
  Finalized: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Rejected: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Cancelled: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Unknown: 'bg-amber-100 text-amber-800 ring-amber-600/20',
};

/** Human-readable status labels (wire value is PascalCase, BR-6). */
export const RUN_STATUS_LABELS: Record<PayrollRunStatus | 'Unknown', string> = {
  Queued: 'Queued',
  Processing: 'Processing',
  ReviewPending: 'Review pending',
  AwaitingApproval: 'Awaiting approval',
  Approved: 'Approved',
  Finalized: 'Finalized',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  // ISSUE-317 / DF-12: label for a backend-tolerated corrupt row.
  Unknown: 'Unknown',
};

/**
 * ISSUE-154: friendly messages for the 409 error codes returned by the run
 * Cancel / Re-run endpoints (ApiResponse.Code). The FE gates the buttons to match
 * the BE guard matrix, but the server is the authority — a race (e.g. the run
 * finalized in another tab) surfaces one of these codes, mapped here to a
 * human-readable toast. `run_not_found` (404) is included for completeness.
 */
export const RUN_ACTION_ERROR_MESSAGES: Record<string, string> = {
  run_finalized:
    'This run is finalized and can no longer be cancelled or re-run.',
  run_in_progress:
    'This run is still processing — wait for it to finish before cancelling.',
  run_cancelled: 'This run is already cancelled.',
  run_already_cancelled: 'This run is already cancelled.',
  run_not_rerunnable: 'Only a run that is pending review can be re-run.',
  run_not_found: 'This payroll run no longer exists.',
};

/** Map an ApiResponse error `code` to a friendly message; falls back to a generic line. */
export function runActionErrorMessage(code: string | null | undefined): string {
  return (
    (code ? RUN_ACTION_ERROR_MESSAGES[code] : undefined) ??
    'That action could not be completed. Please refresh and try again.'
  );
}

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

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// `http.get<IPayrollRun>(…)` was an unchecked ASSERTION, not a check: TypeScript believed the
// annotation and the server sent something else. These aliases bind the view-models above to the
// GENERATED contract, so a backend rename becomes a compile error here instead of an `undefined`
// cell in the runs table (or, as it did for `initiateRun`, a route to `/payroll/runs/undefined`).
//
// DEFAULTING POLICY (payroll is money — a default is a decision):
//  - STATUS is never coerced to a meaningful state. An absent or unrecognised status maps to the
//    `Unknown` sentinel the backend itself emits (ENH-021 / ISSUE-317), which `RUN_STATUS_BADGE`
//    and `RUN_STATUS_LABELS` already render as an amber "Unknown" badge. It must NOT fall back to
//    `Queued` (that would enable Cancel and light a stepper node) and certainly not to
//    `Approved`/`Finalized`.
//  - Employee counts and money totals default to 0: they are server-computed aggregates that are
//    genuinely 0 before the run completes, and the UI draws no distinction between "zero" and
//    "no value" for them (every binding is a `| number` pipe, not a "—" placeholder).
//  - `completedAt` / `initiatedByName` default to null — the UI renders "—" for those.

export type PayrollRunWire = Schema<'PayrollPayrollRunDto'>;
export type PayrollRunAcceptedWire = Schema<'PayrollPayrollRunAcceptedDto'>;
export type PayrollRunProgressWire = Schema<'PayrollPayrollRunProgressDto'>;

/**
 * The status strings the FE knows. Kept in sync with the `PayrollRunStatus` union above by
 * construction (the array is typed as that union, so a missing member is a compile error).
 */
const KNOWN_RUN_STATUSES: readonly PayrollRunStatus[] = [
  'Queued',
  'Processing',
  'ReviewPending',
  'AwaitingApproval',
  'Approved',
  'Finalized',
  'Rejected',
  'Cancelled',
];

/**
 * The sentinel used for an absent or unrecognised wire status. The backend's C# enum has a real
 * `Unknown = 99` member (ENH-021, emitted by the tolerant enum-read converter) that the FE
 * `PayrollRunStatus` union does NOT carry — see the OUT-OF-LANE note for that drift. Until the
 * union is widened (it is imported by `audit.models.ts` and by component inputs typed
 * `PayrollRunStatus`, so widening is a cross-file decision), this maps through with a single
 * documented cast. Every consumer is safe with it at runtime: `RUN_STATUS_BADGE` /
 * `RUN_STATUS_LABELS` have an `Unknown` key, `canGeneratePayslipsFor` returns false, the stepper
 * `findIndex` returns -1, and every `=== 'Finalized'`-style guard is false — i.e. least-claiming.
 */
const UNKNOWN_RUN_STATUS = 'Unknown' as PayrollRunStatus;

/**
 * Narrow the wire's `status?: string | null` to `PayrollRunStatus`. NEVER coerces an unknown value
 * to a meaningful lifecycle state — see `UNKNOWN_RUN_STATUS`.
 */
export function mapPayrollRunStatus(
  raw: string | null | undefined,
): PayrollRunStatus {
  return (KNOWN_RUN_STATUSES as readonly string[]).includes(raw ?? '')
    ? (raw as PayrollRunStatus)
    : UNKNOWN_RUN_STATUS;
}

/**
 * `PayrollRunDto` → `IPayrollRun` (GET /payroll/runs, GET /payroll/runs/{id}).
 *
 * NOTE — `initiatedByName` has NO wire source on this DTO: the contract carries only
 * `initiatedBy` (a uuid). It is defaulted to null (the UI renders "—"), NOT filled with the raw
 * uuid, which would put a guid in the "Initiated by" column. Flagged OUT-OF-LANE.
 */
export function mapPayrollRun(w: PayrollRunWire): IPayrollRun {
  return {
    id: w.id ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    status: mapPayrollRunStatus(w.status),
    totalEmployees: w.totalEmployees ?? 0,
    processedEmployees: w.processedEmployees ?? 0,
    skippedEmployees: w.skippedEmployees ?? 0,
    totalGross: w.totalGross ?? 0,
    totalDeductions: w.totalDeductions ?? 0,
    totalNet: w.totalNet ?? 0,
    // No wire source on PayrollRunDto — see the note above.
    initiatedByName: null,
    initiatedAt: w.initiatedAt ?? '',
    completedAt: w.completedAt ?? null,
  };
}

/**
 * `PayrollRunAcceptedDto` → `IPayrollRun` (POST /payroll/runs [202], POST …/cancel, POST …/rerun).
 *
 * These three endpoints do NOT return a full run — the contract's accepted DTO carries only
 * `{ runId, status, payMonth, payYear }`. The FE previously annotated them `IPayrollRun`, so
 * `.id` was `undefined`; `payroll-runs.component.onCreated` then navigated to
 * `/payroll/runs/undefined` after starting a run. **`runId` → `id` is the rename that fixes it.**
 *
 * The remaining `IPayrollRun` fields have no wire source here. They are zero/null-filled rather
 * than invented, and no caller renders them (all three call sites refetch the run) — but the
 * honest shape is a narrower accepted view-model, which would change component signatures.
 * Flagged OUT-OF-LANE.
 */
export function mapPayrollRunAccepted(w: PayrollRunAcceptedWire): IPayrollRun {
  return {
    // RENAME: wire `runId` → view-model `id`.
    id: w.runId ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    status: mapPayrollRunStatus(w.status),
    // Not carried by the accepted DTO — placeholders, not claims. Callers refetch the run.
    totalEmployees: 0,
    processedEmployees: 0,
    skippedEmployees: 0,
    totalGross: 0,
    totalDeductions: 0,
    totalNet: 0,
    initiatedByName: null,
    initiatedAt: '',
    completedAt: null,
  };
}

/**
 * `PayrollRunProgressDto` → `IPayrollRunProgress` (GET /payroll/runs/{id}/progress, FR-6).
 * The wire also carries `isComplete`, which the view-model does not model — `streamProgress`
 * derives completion from the status instead, so it is deliberately dropped (not a data loss:
 * `isComplete` is a function of the same status).
 */
export function mapPayrollRunProgress(
  w: PayrollRunProgressWire,
): IPayrollRunProgress {
  return {
    runId: w.runId ?? '',
    status: mapPayrollRunStatus(w.status),
    processedEmployees: w.processedEmployees ?? 0,
    totalEmployees: w.totalEmployees ?? 0,
    skippedEmployees: w.skippedEmployees ?? 0,
  };
}
