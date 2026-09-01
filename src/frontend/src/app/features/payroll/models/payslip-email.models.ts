/**
 * US-PAY-011: Models for BULK PAYSLIP EMAIL DISTRIBUTION — sending payslip PDFs to
 * employees by email after a payroll run is Finalized, polling distribution
 * progress, and surfacing the per-employee delivery summary (Sent / Failed /
 * Skipped) with selective re-send.
 *
 * Backend endpoints (REAL contract — reconciled to the built + tested backend).
 * `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`:
 *   POST /payroll/runs/:runId/payslips/send-emails        - enqueue distribution (AC-1, 202)
 *                                                           body { confirm } (FR-7/BR-5 re-send)
 *                                                           → IPayslipDistributionAccepted (NOT status)
 *   POST /payroll/runs/:runId/payslips/resend-emails      - selective / failed re-send (FR-4)
 *                                                           body { onlyFailed } | { employeeIds }
 *                                                           → IPayslipDistributionAccepted (NOT status)
 *   GET  /payroll/runs/:runId/payslips/distribution-summary - distribution progress + summary
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header) and use withCredentials for the httpOnly cookie auth. The backend stamps
 * tenant_id, enforces RLS, and ensures no cross-employee/cross-tenant leakage
 * (AC-5) — the FE never sends tenant info.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the service consumes BARE payloads.
 *
 * ENUM CASING (US-PLT-003 — critical): `EmailDeliveryStatus` is a PascalCase string
 * union matching the C# member names (global JsonStringEnumConverter); statuses
 * arrive as STRINGS, never integers.
 */

import type { Schema } from '@core/api';

// ─── Delivery status enum (§7 status column, FR-5) ─────────────

/**
 * Per-employee email delivery status (§7 `payslip_email_log.status`). Matches the
 * C# `EmailDeliveryStatus` members:
 *   `Queued`  - pending send (job not yet reached this employee).
 *   `Sent`    - SMTP accepted the message (delivery confirmation is SMTP-accept,
 *               not inbox delivery — out of scope per §10).
 *   `Failed`  - permanently failed after Polly retries (FR-4/AC-4), retryable.
 *   `Skipped` - no email address on file (AC-3) or opted out (BR-3).
 */
export type EmailDeliveryStatus = 'Queued' | 'Sent' | 'Failed' | 'Skipped';

/** Tailwind badge classes per delivery status (§8 green/red/amber). Single source. */
export const EMAIL_STATUS_BADGE: Record<EmailDeliveryStatus, string> = {
  Queued: 'bg-neutral-100 text-neutral-600 ring-neutral-500/20',
  Sent: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Failed: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Skipped: 'bg-amber-50 text-amber-700 ring-amber-600/20',
};

/** Human-readable delivery status labels (wire value is PascalCase). */
export const EMAIL_STATUS_LABELS: Record<EmailDeliveryStatus, string> = {
  Queued: 'Queued',
  Sent: 'Sent',
  Failed: 'Failed',
  Skipped: 'Skipped',
};

// ─── Per-employee distribution row (summary lists, §8) ─────────

/**
 * One employee's email-delivery record, shown in the expandable Sent / Failed /
 * Skipped lists of the distribution summary card (§8). Carries the denormalized
 * employee display fields, the (possibly empty) recipient email, the status and —
 * for failures — the failure reason (FR-4/AC-4 surfaced to HR).
 */
export interface IEmployeeDistribution {
  employeeId: string;
  employeeName: string;
  employeeNo: string;
  /** The recipient address; empty/null when Skipped (no email on file, AC-3). */
  recipientEmail: string | null;
  status: EmailDeliveryStatus;
  /** Failure reason for a permanently-failed delivery (FR-4); null otherwise. */
  failureReason: string | null;
  /** ISO timestamp the email was sent; null until/unless Sent (§7 sent_at). */
  sentAt: string | null;
  /** Number of send attempts so far for this recipient (backend retry count). */
  retryCount: number;
}

// ─── Distribution status + summary (BR-6, §8) ──────────────────

/**
 * Aggregate distribution status for a run, driving the progress bar (§8: "sent /
 * total") and the post-completion summary card (BR-6: Total / Sent / Failed /
 * Skipped / Queued). The FE polls this while the job runs and stops once it
 * settles.
 *
 * `isSending` is the authoritative "still running" flag the poll loop checks; it is
 * true while the Hangfire job (AC-1, FR-2) is dispatching emails and false once
 * every employee has reached a terminal state (Sent / Failed / Skipped).
 *
 * `hasSent` is BR-5/FR-7: true once a distribution has already run for this run, so
 * the FE requires an explicit confirm before re-sending (avoids duplicate emails).
 */
export interface IPayslipDistributionStatus {
  runId: string;
  /** True while the background distribution job is running (drives the poll loop). */
  isSending: boolean;
  /** True once payslips have been sent at least once (FR-7/BR-5 duplicate guard). */
  hasSent: boolean;
  totalEmployees: number;
  emailsSent: number;
  emailsFailed: number;
  emailsSkipped: number;
  emailsQueued: number;
  /** ISO timestamp the job started; null before the first send (§7 started_at). */
  startedAt: string | null;
  /** ISO timestamp the job finished; null while still running (§7 completed_at). */
  completedAt: string | null;
  /** Per-employee delivery records for the expandable summary lists (§8). */
  recipients: IEmployeeDistribution[];
}

/**
 * 202 Accepted payload from `send-emails` / `resend-emails` (NOT the status). The
 * backend enqueues the distribution job and returns the queued count; the FE then
 * starts polling `distribution-summary` to drive the progress bar.
 *
 * `resend` is true when this was a duplicate/re-send (the run already had a send).
 */
export interface IPayslipDistributionAccepted {
  runId: string;
  queuedCount: number;
  resend: boolean;
}

// ─── Send / re-send requests ───────────────────────────────────

/**
 * Body for `POST payslips/send-emails` (AC-1). `confirm` is the FR-7/BR-5 duplicate-send
 * acknowledgement: the backend rejects a re-send unless `confirm: true` once
 * payslips were already sent for the run.
 */
export interface ISendPayslipsRequest {
  confirm: boolean;
}

/**
 * Body for `POST payslips/resend-emails` (FR-4). Either re-send to every failed
 * delivery (`onlyFailed: true`) OR a selective set of employees (`employeeIds`).
 */
export interface IResendRequest {
  onlyFailed?: boolean;
  employeeIds?: string[];
}

// ─── Pure helpers (unit-tested) ────────────────────────────────

/**
 * Progress percentage for the distribution bar (§8). Counts every terminal
 * delivery (sent + failed + skipped) against the total so the bar reaches 100% even
 * when some employees are skipped/failed. 0 when there is nothing to send.
 */
export function distributionPercent(s: IPayslipDistributionStatus | null): number {
  if (!s || s.totalEmployees <= 0) {
    return 0;
  }
  const done = s.emailsSent + s.emailsFailed + s.emailsSkipped;
  return Math.min(100, Math.round((done / s.totalEmployees) * 100));
}

/** The recipients with a given delivery status, for the expandable lists (§8). */
export function recipientsByStatus(
  s: IPayslipDistributionStatus | null,
  status: EmailDeliveryStatus,
): IEmployeeDistribution[] {
  return (s?.recipients ?? []).filter((r) => r.status === status);
}

// ─── Generated-contract wire types + mappers (GAP-S1) ──────────
//
// `http.get<IPayslipDistributionStatus>(…)` was an unchecked assertion, not a check. These aliases
// bind the view-models above to the GENERATED contract, so a backend rename becomes a compile error
// here instead of a distribution progress bar that silently reads `undefined`.
//
// GOOD NEWS, verified not assumed: this is the one surface of the three in this batch whose
// hand-written interfaces already agree with the contract field-for-field —
// `PayrollPayslipDistributionSummaryDto` → `IPayslipDistributionStatus`,
// `PayrollPayslipRecipientStatusDto` → `IEmployeeDistribution`,
// `PayrollPayslipDistributionAcceptedDto` → `IPayslipDistributionAccepted`. NO renames were needed.
// The mappers still earn their place: they pin the agreement so the next backend rename breaks the
// build, and they replace `undefined` leaking into the progress/summary UI with explicit, documented defaults.
//
// Request bodies also verified: `ISendPayslipsRequest { confirm }` matches
// `PayrollSendPayslipEmailsRequest`, and `IResendRequest { onlyFailed?, employeeIds? }` matches
// `PayrollResendPayslipEmailsRequest` (there `onlyFailed` is a non-nullable bool, so omitting it on
// a selective re-send binds to `false` server-side — which is exactly the intent).
//
// DEFAULTING POLICY (payroll is money — a default is a decision):
//  - `isSending` defaults to TRUE. It is a RUNNING flag, so its completion reading is `!isSending`;
//    `streamDistributionStatus` ends its poll on `isSending === false` and the component then
//    presents the run as distributed. An absent flag must never announce that a distribution
//    finished. (Trade-off acknowledged: were the server to persistently omit it, the bar would poll
//    on. `PayslipDistributionSummaryDto` DECLARES `isSending` in its OpenAPI `required` list — along
//    with `hasSent`, `recipients`, `runId`, `totalEmployees` and the four email counts — so it is
//    non-optional in the generated type and this is defence against a malformed payload, not a live
//    hang. The blanket "every generated property is optional" note in core/api/index.ts does not
//    hold for this schema family.)
//  - `hasSent` defaults to FALSE, the opposite direction, and deliberately so. It is the BR-5/FR-7
//    duplicate-send guard: the component sends `confirm: !!status.hasSent` and only requires the
//    acknowledgement checkbox when it is true. Defaulting it to TRUE would make the FE assert
//    `confirm: true` on a payload that never said a send had happened — i.e. hand the backend the
//    very consent flag that unlocks a duplicate mailing to every employee. FALSE claims no consent,
//    leaves the server's 409 `resend_requires_confirmation` as the real guard, and the worst case is
//    an extra round-trip rather than a duplicate payslip in every inbox.
//  - Counts (`totalEmployees`, `emailsSent/Failed/Skipped/Queued`, `retryCount`, `queuedCount`) are
//    server-computed integers feeding `distributionPercent()` arithmetic, and the UI draws no
//    distinction between zero and no-value, so `?? 0` is correct.
//  - The genuinely optional fields on this surface are `startedAt` / `completedAt` on the summary
//    and `failureReason` / `sentAt` on a recipient; `status` is `required` but `nullable`, so a
//    null status is a reachable payload, not a hypothetical one.
//  - `resend` on the 202 defaults to FALSE — the least-claiming reading of "this was a duplicate".
//  - Timestamps (`startedAt` / `completedAt` / `sentAt`), `failureReason` and `recipientEmail`
//    default to NULL: all are `nullable` in the schema and the UI renders "no value" distinctly
//    (`recipientEmail` empty is precisely the AC-3 "no email on file" case).
//  - `status` is `string | null` on the wire. An absent value is passed through UNCHANGED, never
//    coerced: defaulting to `Sent` would tell an employee's row that a payslip reached them, and
//    defaulting to `Failed` would push a healthy row into the Re-send All Failed set — a real
//    duplicate-email risk. An unrecognised value misses the badge/label `Record` lookups and
//    renders blank, and `recipientsByStatus` puts it in no list.
//
// FE union vs contract values: `EmailDeliveryStatus` (Queued | Sent | Failed | Skipped) matches
// `HRM.Domain/Payroll/EmailDeliveryStatus.cs` value-for-value (a static class of string consts, not
// a C# enum, which is why the OpenAPI document types it as a bare `string`). No FE-only members.

export type EmployeeDistributionWire =
  Schema<'PayrollPayslipRecipientStatusDto'>;
export type PayslipDistributionStatusWire =
  Schema<'PayrollPayslipDistributionSummaryDto'>;
export type PayslipDistributionAcceptedWire =
  Schema<'PayrollPayslipDistributionAcceptedDto'>;
export type SendPayslipsRequestWire = Schema<'PayrollSendPayslipEmailsRequest'>;
export type ResendRequestWire = Schema<'PayrollResendPayslipEmailsRequest'>;

export function mapEmployeeDistribution(
  w: EmployeeDistributionWire,
): IEmployeeDistribution {
  return {
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeeNo: w.employeeNo ?? '',
    recipientEmail: w.recipientEmail ?? null,
    // Passed through, NOT defaulted: coercing would either claim delivery or queue a re-send.
    status: (w.status ?? '') as EmailDeliveryStatus,
    failureReason: w.failureReason ?? null,
    sentAt: w.sentAt ?? null,
    retryCount: w.retryCount ?? 0,
  };
}

export function mapPayslipDistributionStatus(
  w: PayslipDistributionStatusWire,
): IPayslipDistributionStatus {
  return {
    runId: w.runId ?? '',
    // Fails OPEN: never announce that a distribution finished on a missing flag.
    isSending: w.isSending ?? true,
    // Fails CLOSED: never assert the duplicate-send consent the UI derives from this.
    hasSent: w.hasSent ?? false,
    totalEmployees: w.totalEmployees ?? 0,
    emailsSent: w.emailsSent ?? 0,
    emailsFailed: w.emailsFailed ?? 0,
    emailsSkipped: w.emailsSkipped ?? 0,
    emailsQueued: w.emailsQueued ?? 0,
    startedAt: w.startedAt ?? null,
    completedAt: w.completedAt ?? null,
    recipients: (w.recipients ?? []).map(mapEmployeeDistribution),
  };
}

export function mapPayslipDistributionAccepted(
  w: PayslipDistributionAcceptedWire,
): IPayslipDistributionAccepted {
  return {
    runId: w.runId ?? '',
    queuedCount: w.queuedCount ?? 0,
    resend: w.resend ?? false,
  };
}
