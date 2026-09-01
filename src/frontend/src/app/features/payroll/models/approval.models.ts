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

import type { Schema } from '@core/api';
import { IPayrollRun, mapPayrollRunStatus } from './payroll-run.models';

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

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// THIS MIGRATION FOUND A LIVE DEFECT ON THE PAYROLL APPROVAL SCREEN. `IApprovalSummary.exceptions` was
// declared `IPayrollException[]` — objects with `severity`/`message`/`employeeName`. **The API sends
// `string[]`.** `http.get<IApprovalSummary>(…)` asserted the shape instead of checking it, so every row's
// `ex.message` and `ex.severity` were `undefined`: the approver saw "Exceptions (3)" above three blank
// amber rows — the exact items they are supposed to read before approving a payroll run.
//
// Severity was never carried by the API at all (`PayrollApprovalService` builds plain sentences: skipped
// employees, negative net salary, no employees processed). Every exception is therefore mapped as a
// Warning, which is what the UI already rendered — `undefined !== 'Error'` took the amber branch — so the
// styling is unchanged and only the text appears. Inventing an Error/Warning split the backend does not
// make would be putting a judgement in the UI that no data supports.

export type ApprovalResultWire = Schema<'PayrollPayrollApprovalResultDto'>;
export type ApprovalSummaryWire = Schema<'PayrollPayrollApprovalSummaryDto'>;
export type ApprovalHistoryWire = Schema<'PayrollPayrollApprovalHistoryDto'>;
export type PendingApprovalWire = Schema<'PayrollPendingApprovalDto'>;

/**
 * One API exception sentence as the panel renders it.
 *
 * `employeeName` is null because the wire carries no per-employee attribution — the sentences name counts
 * ("3 employee(s) were skipped"), not people.
 */
export function mapPayrollException(message: string): IPayrollException {
  return { severity: 'Warning', message, employeeName: null };
}

export function mapApprovalSummary(w: ApprovalSummaryWire): IApprovalSummary {
  return {
    runId: w.runId ?? '',
    totalEmployees: w.totalEmployees ?? 0,
    totalGross: w.totalGross ?? 0,
    totalDeductions: w.totalDeductions ?? 0,
    totalStatutory: w.totalStatutory ?? 0,
    totalNet: w.totalNet ?? 0,
    // null, not 0: the card distinguishes "no prior run to compare" from "the prior run was zero".
    previousMonthTotalNet: w.previousMonthTotalNet ?? null,
    variancePercentage: w.variancePercentage ?? null,
    exceptions: (w.exceptions ?? []).map(mapPayrollException),
  };
}

/**
 * The action strings the FE knows. Typed as the union by construction, so dropping a member here is a
 * compile error rather than a silently unstyled timeline row.
 */
const KNOWN_APPROVAL_ACTIONS: readonly ApprovalAction[] = [
  'Submitted',
  'Approved',
  'Rejected',
  'Returned',
  'Escalated',
];

/**
 * Pass the wire's `action?: string | null` through WITHOUT coercing it to a known member.
 *
 * An absent or unrecognised action must NOT be stamped `Submitted` (or, worse, `Approved`): this is the
 * approval AUDIT TRAIL for a payroll run, and mislabelling a row here misrepresents who did what to a
 * payment. An unknown value keeps its raw string, so `APPROVAL_ACTION_LABELS[…]` renders nothing rather
 * than the wrong thing, and none of the `h.action === 'Approved'` colour guards in the timeline fire.
 * The single cast is the honest expression of "the server said something this union does not model" —
 * `Delegated` is exactly that today (see the OUT-OF-LANE enum-drift finding).
 */
function passThroughApprovalAction(
  raw: string | null | undefined,
): ApprovalAction {
  return (KNOWN_APPROVAL_ACTIONS as readonly string[]).includes(raw ?? '')
    ? (raw as ApprovalAction)
    : ((raw ?? '') as ApprovalAction);
}

/**
 * Maps one approval-history row.
 *
 * `actorName` is null on purpose: the wire carries only `actorUserId` (a GUID), so the timeline has never
 * had a name to show and renders its `|| '—'` fallback. Synthesising one here would be inventing data;
 * resolving it needs an API change. Filed — see the D1 payroll findings.
 */
export function mapApprovalHistoryEntry(
  w: ApprovalHistoryWire,
): IApprovalHistoryEntry {
  return {
    id: w.id ?? '',
    stepNumber: w.stepNumber ?? 0,
    action: passThroughApprovalAction(w.action),
    actorName: null,
    comments: w.comments ?? null,
    actedAt: w.actedAt ?? '',
    ipAddress: w.ipAddress ?? null,
  };
}

/**
 * `PendingApprovalDto` → `IPayrollRun` for the approver's queue card (§8, DF-14).
 *
 * This mapper already existed as a private `toPayrollRun` in `payroll-approval.service.ts` with a
 * hand-written `IPendingApprovalDto` interface beside it; it now binds to the GENERATED contract instead,
 * so a backend rename is a compile error here. **RENAME: wire `runId` → view-model `id`** (the queue card
 * routes on `r.id`).
 *
 * Two view-model fields are DERIVED, not sent — kept from the original mapper because they are exact
 * identities, not guesses, and neither is rendered by the queue card:
 *   `totalDeductions = totalGross - totalNet` and `skippedEmployees = totalEmployees - processedEmployees`.
 * `completedAt` is null: a run awaiting approval has not completed. `currentApprovalStep` /
 * `totalApprovalSteps` are carried by the wire but have no `IPayrollRun` field — see the OUT-OF-LANE note.
 */
export function mapPendingApproval(w: PendingApprovalWire): IPayrollRun {
  const totalGross = w.totalGross ?? 0;
  const totalNet = w.totalNet ?? 0;
  const totalEmployees = w.totalEmployees ?? 0;
  const processedEmployees = w.processedEmployees ?? 0;
  return {
    // RENAME: wire `runId` → view-model `id`.
    id: w.runId ?? '',
    payMonth: w.payMonth ?? 0,
    payYear: w.payYear ?? 0,
    // Never coerced to Approved/Finalized — see mapPayrollRunStatus.
    status: mapPayrollRunStatus(w.status),
    totalEmployees,
    processedEmployees,
    skippedEmployees: totalEmployees - processedEmployees,
    totalGross,
    totalDeductions: totalGross - totalNet,
    totalNet,
    // This DTO — unlike PayrollRunDto — really does carry the initiator's display name.
    initiatedByName: w.initiatedByName ?? null,
    initiatedAt: w.initiatedAt ?? '',
    completedAt: null,
  };
}

/**
 * `PayrollApprovalResultDto` → `IPayrollRun`, for submit / approve / reject / return / finalize.
 *
 * These five endpoints return a RESULT, not a run: `{ runId, status, action, currentApprovalStep,
 * totalApprovalSteps, workflowInstanceId }`. The FE annotated them `IPayrollRun`, so every total and
 * count on the returned object was `undefined`. No caller reads them today — `payroll-run-detail`'s
 * `runAction()` discards the value and refetches — so the remaining fields are zero/null placeholders,
 * NOT claims. **RENAME: wire `runId` → view-model `id`.** The honest fix is a narrower result view-model,
 * which would change component signatures; flagged OUT-OF-LANE.
 */
export function mapApprovalResultToRun(w: ApprovalResultWire): IPayrollRun {
  return {
    // RENAME: wire `runId` → view-model `id`.
    id: w.runId ?? '',
    // Not carried by the result DTO — placeholders, not claims. Callers refetch the run.
    payMonth: 0,
    payYear: 0,
    status: mapPayrollRunStatus(w.status),
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
 * The approval-step position an action result reports (`2 of 3`). Exposed because the result DTO
 * genuinely carries it and `IPayrollRun` has nowhere to put it — dropping it silently is what the
 * previous unchecked cast did.
 */
export interface IApprovalStepPosition {
  currentApprovalStep: number | null;
  totalApprovalSteps: number | null;
}

/** Read the step position off an action result. Absent values stay null — never 0-of-0. */
export function approvalStepPosition(
  w: ApprovalResultWire,
): IApprovalStepPosition {
  return {
    currentApprovalStep: w.currentApprovalStep ?? null,
    totalApprovalSteps: w.totalApprovalSteps ?? null,
  };
}
