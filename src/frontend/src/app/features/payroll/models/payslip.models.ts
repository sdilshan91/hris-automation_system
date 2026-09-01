/**
 * US-PAY-004: Models for individual payslips within a payroll run — the payslip
 * list (§8 Notion table), PDF generation status, and the generation progress bar.
 *
 * Backend endpoints, VERIFIED against contracts/openapi/hrm-v1.json (they were previously assumed;
 * all seven exist). `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   GET    /payroll/runs/:runId/payslips                  - list payslips (table, §8)
 *   POST   /payroll/runs/:runId/payslips/generate         - enqueue generation (AC-1), 202
 *   POST   /payroll/runs/:runId/payslips/regenerate       - overwrite existing (AC-5), 202
 *   POST   /payroll/runs/:runId/payslips/:employeeId/retry - retry one slip (FR-8), 202
 *   GET    /payroll/runs/:runId/payslips/status           - generation progress (§8)
 *   GET    /payroll/runs/:runId/payslips/:employeeId/download - single PDF (blob, AC-3)
 *   GET    /payroll/runs/:runId/payslips/download-zip      - bulk ZIP (blob, AC-3)
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend stamps
 * tenant_id, enforces RLS, and validates blob paths against traversal (AC-4,
 * NFR-6) — the FE never sends tenant info.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads (binary downloads are
 * unaffected — they bypass the JSON envelope).
 *
 * ENUM CASING (US-PLT-003 — critical): `PdfStatus` is a PascalCase string union
 * matching the C# member names (global JsonStringEnumConverter); statuses arrive
 * as STRINGS, never integers.
 */

import type { Schema } from '@core/api';

// ─── PDF status enum (§7 pdf_status, FR-7) ─────────────────────

/**
 * Per-payslip PDF generation status (§7 `pdf_status` column). Matches the C#
 * `PdfStatus` enum members. `Pending` = not yet rendered (or queued); `Generated`
 * = PDF stored in blob storage; `Failed` = render errored (retryable, FR-8).
 */
export type PdfStatus = 'Pending' | 'Generated' | 'Failed';

/** Tailwind badge classes per PDF status (§8 status badge). Single source of truth. */
export const PDF_STATUS_BADGE: Record<PdfStatus, string> = {
  Pending: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Generated: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Failed: 'bg-rose-50 text-rose-700 ring-rose-600/20',
};

/** Human-readable PDF status labels (wire value is PascalCase). */
export const PDF_STATUS_LABELS: Record<PdfStatus, string> = {
  Pending: 'Pending',
  Generated: 'Generated',
  Failed: 'Failed',
};

// ─── Payslip row (list view, §8) ───────────────────────────────

/**
 * One payslip as shown in the run-detail Notion-style table (§8): Employee Name |
 * Employee No | Department | Net Salary | PDF Status | Actions. Carries the slip id
 * (for per-employee download/preview) and the denormalized employee display fields
 * (point-in-time snapshot, BR-2). `netSalary` is the slip's net pay (§7 summary).
 */
export interface IPayslip {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeNo: string;
  department: string | null;
  netSalary: number;
  pdfStatus: PdfStatus;
  /** ISO timestamp, null until the PDF is generated (§7 pdf_generated_at). */
  pdfGeneratedAt: string | null;
}

// ─── Generation status (progress bar, §8) ──────────────────────

/**
 * Aggregate PDF-generation status for a run, driving the status bar (§8: "4,850 /
 * 5,000 generated"). The FE polls this while generation is in progress and stops
 * once it settles (no longer generating). `failedCount` surfaces partial failures
 * (FR-8 — retryable individually).
 *
 * `isGenerating` is the authoritative "still running" flag the poll loop checks; it
 * is true while the Hangfire job (AC-1, FR-4) is rendering and false once every
 * payslip has reached a terminal state (Generated or Failed).
 */
export interface IPayslipGenerationStatus {
  runId: string;
  /** True while the background generation job is running (drives the poll loop). */
  isGenerating: boolean;
  totalCount: number;
  /**
   * MISNAMED, and kept only because components bind it: despite the name this is the number of
   * payslips ALREADY GENERATED (§8 renders `{{ queuedCount }} / {{ totalCount }} generated`, and
   * `payslip-distribution` gates Send on `queuedCount > 0`).
   *
   * GAP-010, corrected: the earlier note here claimed the STATUS endpoint sends `queuedCount`. It
   * does not — `PayslipGenerationStatusDto` sends `generated`; only the *accepted* DTO (the 202 from
   * generate/regenerate) sends `queuedCount`, and there it means "how many were enqueued", the
   * opposite of "generated". Both are now mapped explicitly (see the mappers at the bottom).
   */
  queuedCount: number;
  failedCount: number;
  pendingCount: number;
}

// ─── Generated-contract wire types + mappers (GAP-S1) ──────────
//
// `http.get<IPayslip[]>(…)` was an unchecked assertion, not a check. These aliases bind the
// view-models above to the GENERATED contract, so a backend rename becomes a compile error here
// instead of a blank cell in the §8 table or a progress bar that never moves.
//
// The generation surface has THREE wire shapes and the FE had exactly one hand-written interface for
// all of them, so almost every field was reaching the UI as `undefined`:
//
//  1. `PayrollPayslipListItemDto`      — the row. `slipId`, not `id`.
//  2. `PayrollPayslipGenerationAcceptedDto` — the 202 from generate/regenerate: `{ runId,
//     queuedCount, regenerated }`. It carries NO completion flag and NO totals.
//  3. `PayrollPayslipGenerationStatusDto`   — the poll snapshot: `{ runId, totalSlips, generated,
//     pending, failed, isComplete }`. Every name differs from the view-model, and `isComplete` is
//     the INVERSE of `isGenerating`.
//
// RENAMES the mappers absorb (view-model ← wire) — NEVER rename the view-model to fit:
//   id           ← slipId
//   isGenerating ← !isComplete        (inverted)
//   totalCount   ← totalSlips
//   queuedCount  ← generated          (the view-model name is wrong; see IPayslipGenerationStatus)
//   failedCount  ← failed
//   pendingCount ← pending
//
// DEFAULTING POLICY (payroll is money — a default is a decision):
//  - `isGenerating` derives from `isComplete ?? false`, i.e. an ABSENT completion flag means STILL
//    RUNNING. This is the least-claiming direction: `streamGenerationStatus` completes the poll on
//    `isGenerating === false` and the component then toasts "Payslip generation finished" and
//    reloads. Telling an operator the run finished because a field was missing is exactly the
//    failure this migration exists to stop. NOTE: unlike most DTOs in this codebase,
//    `PayslipGenerationStatusDto` and `PayslipGenerationAcceptedDto` DO declare `required` in the
//    OpenAPI document (`isComplete`, `totalSlips`, `generated`, `pending`, `failed`, `runId`;
//    `queuedCount`, `regenerated`, `runId`), so these fields are non-optional in the generated type
//    and the fallbacks are defence against a malformed/degraded payload rather than a routine one.
//    The blanket "every generated property is optional" note in core/api/index.ts does not hold for
//    this schema family.
//  - Counts (`totalCount` / `queuedCount` / `failedCount` / `pendingCount`) are server-computed
//    integers feeding arithmetic in `progressPercent()`, and the UI draws no distinction between
//    zero and no-value, so `?? 0` is correct.
//  - `netSalary` likewise `?? 0`: `required` and non-nullable in the schema, always computed for a
//    persisted slip. `department`, `pdfStatus`, `pdfGeneratedAt` and `pdfFileSizeBytes` are the only
//    genuinely optional fields on `PayslipListItemDto`.
//  - `pdfStatus` is `string | null` on the wire (Swashbuckle emits the JsonStringEnumConverter
//    members as a bare `string`). An absent value is passed through UNCHANGED, never coerced:
//    defaulting to `Generated` would claim a PDF exists, and defaulting to `Failed` would put a red
//    badge and a Retry button on a healthy slip (exactly the admin-slice mistake). An unrecognised
//    value misses the badge/label `Record` lookups and renders blank — visibly wrong beats
//    confidently wrong.
//  - `department` / `pdfGeneratedAt` default to NULL: both are `nullable` in the schema and the UI
//    renders "no value" differently from an empty string.
//
// FE union vs contract values: `PdfStatus` (Pending | Generated | Failed) matches
// `HRM.Domain/Payroll/PayslipPdfStatus.cs` value-for-value (it is a static class of string consts,
// not a C# enum, which is why the OpenAPI document types it as a bare `string`). No FE-only members.

export type PayslipWire = Schema<'PayrollPayslipListItemDto'>;
export type PayslipGenerationAcceptedWire =
  Schema<'PayrollPayslipGenerationAcceptedDto'>;
export type PayslipGenerationStatusWire =
  Schema<'PayrollPayslipGenerationStatusDto'>;

export function mapPayslip(w: PayslipWire): IPayslip {
  return {
    // RENAME: the wire field is `slipId`. Bound to per-row download/preview/retry actions.
    id: w.slipId ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeeNo: w.employeeNo ?? '',
    department: w.department ?? null,
    netSalary: w.netSalary ?? 0,
    // Passed through, NOT defaulted: coercing would either claim a PDF exists or flag a healthy slip.
    pdfStatus: (w.pdfStatus ?? '') as PdfStatus,
    pdfGeneratedAt: w.pdfGeneratedAt ?? null,
  };
}

/**
 * Map the poll snapshot (`GET …/payslips/status`). Every field is renamed and `isComplete` is
 * inverted; before this mapper `isGenerating` was permanently `undefined`, so the poll loop's
 * `takeWhile(s => s.isGenerating, true)` completed after ONE emission and the UI announced that
 * generation had finished the instant it started.
 */
export function mapPayslipGenerationStatus(
  w: PayslipGenerationStatusWire,
): IPayslipGenerationStatus {
  return {
    runId: w.runId ?? '',
    // INVERTED, and fails OPEN on absence: never claim the job finished on a missing flag.
    isGenerating: !(w.isComplete ?? false),
    totalCount: w.totalSlips ?? 0,
    queuedCount: w.generated ?? 0,
    failedCount: w.failed ?? 0,
    pendingCount: w.pending ?? 0,
  };
}

/**
 * Map the 202 Accepted from generate/regenerate onto the same view-model the progress bar binds.
 *
 * The accepted DTO carries only `{ runId, queuedCount, regenerated }` — no completion flag and no
 * per-state totals — so this is the honest reading of "N slips were just enqueued":
 *  - `isGenerating` is TRUE. A 202 means the job was accepted; the component starts polling
 *    immediately, and claiming completion here would end the poll before it began.
 *  - `queuedCount` (which the UI renders as GENERATED) is 0, NOT the wire's `queuedCount`. Nothing
 *    has been rendered at enqueue time; mapping it straight across would render "500 / 500
 *    generated" the instant the button is clicked.
 *  - `totalCount` and `pendingCount` both take the wire's `queuedCount`: at acceptance, every
 *    enqueued slip is pending and the enqueued count IS the total for this job.
 *  - `failedCount` is 0 — nothing has been attempted yet.
 * `regenerated` has no view-model home (the component already knows which button it pressed).
 */
export function mapPayslipGenerationAccepted(
  w: PayslipGenerationAcceptedWire,
): IPayslipGenerationStatus {
  const queued = w.queuedCount ?? 0;
  return {
    runId: w.runId ?? '',
    isGenerating: true,
    totalCount: queued,
    queuedCount: 0,
    failedCount: 0,
    pendingCount: queued,
  };
}
