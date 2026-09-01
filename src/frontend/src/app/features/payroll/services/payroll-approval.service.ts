import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { IPayrollRun } from '../models/payroll-run.models';
import {
  ApprovalHistoryWire,
  ApprovalResultWire,
  ApprovalSummaryWire,
  IApprovalCommentRequest,
  IApprovalHistoryEntry,
  IApprovalSummary,
  PendingApprovalWire,
  mapApprovalHistoryEntry,
  mapApprovalResultToRun,
  mapApprovalSummary,
  mapPendingApproval,
} from '../models/approval.models';

/**
 * US-PAY-008: Service for the payroll APPROVAL workflow — submitting a run for
 * approval, the approver's approve / reject / return / finalize actions, the
 * approval review summary (totals + variance + exceptions), and the approval
 * history timeline. Also lists the runs Awaiting Approval for the approver queue.
 *
 * Sibling to PayrollRunService; the approval route strings live here ONLY, so a
 * backend contract change is a one-file fix. All requests use withCredentials
 * (httpOnly cookie auth) and are tenant-scoped via the tenantInterceptor
 * (X-Tenant-Subdomain header). The backend stamps tenant_id + audit fields and
 * enforces RLS + the Payroll.Approve permission + maker-checker (BR-5).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Statuses + actions
 * arrive as PascalCase STRINGS (US-PLT-003).
 *
 * REST contract — now PINNED against contracts/openapi/hrm-v1.json (D1 wire-types
 * slice). The response DTO is named per route:
 *   POST /payroll/runs/:id/submit-for-approval  -> PayrollApprovalResultDto (AC-1)
 *   POST /payroll/runs/:id/approve              -> PayrollApprovalResultDto (AC-2)
 *   POST /payroll/runs/:id/reject  { comments } -> PayrollApprovalResultDto (AC-3)
 *   POST /payroll/runs/:id/return  { comments } -> PayrollApprovalResultDto (FR-9)
 *   POST /payroll/runs/:id/finalize             -> PayrollApprovalResultDto (AC-5)
 *   GET  /payroll/runs/:id/approval-summary     -> PayrollApprovalSummaryDto (FR-4)
 *   GET  /payroll/runs/:id/approval-history     -> PayrollApprovalHistoryDto[] (FR-7)
 *   GET  /payroll/approval/pending              -> PendingApprovalDto[] (§8, DF-14)
 *
 * NOTE — the five ACTION routes return a RESULT DTO (`runId` + status + step
 * position), NOT a run. They were annotated `IPayrollRun`, which made `.id` and
 * every total/count on the returned object `undefined`. `mapApprovalResultToRun`
 * maps `runId -> id` and leaves the rest as explicit placeholders. The public
 * signatures stay `Observable<IPayrollRun>` so `payroll-run-detail`'s shared
 * `runAction(action$: Observable<IPayrollRun>)` is unchanged (it discards the
 * value and refetches the run anyway) — the mappers exist so components don't move.
 */
@Injectable({ providedIn: 'root' })
export class PayrollApprovalService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;
  private readonly pendingUrl = `${environment.apiBaseUrl}/payroll/approval/pending`;

  /**
   * Submit a ReviewPending run for approval (AC-1). Creates the workflow instance
   * server-side and moves the run to AwaitingApproval.
   */
  submit(runId: string): Observable<IPayrollRun> {
    return this.http
      .post<ApprovalResultWire>(
        `${this.runsUrl}/${runId}/submit-for-approval`,
        null,
        { withCredentials: true },
      )
      .pipe(map(mapApprovalResultToRun));
  }

  /**
   * Approve the current step (AC-2). When all steps are complete the run becomes
   * Approved (the backend owns the sequential multi-step routing, AC-4).
   */
  approve(runId: string): Observable<IPayrollRun> {
    return this.http
      .post<ApprovalResultWire>(`${this.runsUrl}/${runId}/approve`, null, {
        withCredentials: true,
      })
      .pipe(map(mapApprovalResultToRun));
  }

  /**
   * Reject the run with a reason (AC-3). The reason is REQUIRED (the form enforces
   * it; the backend re-validates). Moves the run to Rejected and notifies HR.
   */
  reject(
    runId: string,
    request: IApprovalCommentRequest,
  ): Observable<IPayrollRun> {
    return this.http
      .post<ApprovalResultWire>(`${this.runsUrl}/${runId}/reject`, request, {
        withCredentials: true,
      })
      .pipe(map(mapApprovalResultToRun));
  }

  /**
   * Return the run to HR with comments WITHOUT formally rejecting it (FR-9). The
   * run goes back to ReviewPending so HR can adjust and re-submit. Comments
   * REQUIRED.
   */
  return(
    runId: string,
    request: IApprovalCommentRequest,
  ): Observable<IPayrollRun> {
    return this.http
      .post<ApprovalResultWire>(`${this.runsUrl}/${runId}/return`, request, {
        withCredentials: true,
      })
      .pipe(map(mapApprovalResultToRun));
  }

  /**
   * Finalize an Approved run (AC-5). Locks all payslip records (FR-8) and moves the
   * run to the terminal Finalized state.
   */
  finalize(runId: string): Observable<IPayrollRun> {
    return this.http
      .post<ApprovalResultWire>(`${this.runsUrl}/${runId}/finalize`, null, {
        withCredentials: true,
      })
      .pipe(map(mapApprovalResultToRun));
  }

  /**
   * The approval review summary for a run (FR-4): totals, statutory subtotal,
   * previous-month net + variance, and the exceptions list. Backs the left summary
   * card + variance comparison on the run-detail approval review layout.
   *
   * The wire's `exceptions` is a `string[]`, not the object list the view-model
   * declared — `mapApprovalSummary` converts each sentence into an
   * `IPayrollException`. Before this, the approver's exceptions panel rendered
   * blank rows. See the comment block in approval.models.ts.
   */
  getApprovalSummary(runId: string): Observable<IApprovalSummary> {
    return this.http
      .get<ApprovalSummaryWire>(`${this.runsUrl}/${runId}/approval-summary`, {
        withCredentials: true,
      })
      .pipe(map(mapApprovalSummary));
  }

  /**
   * The approval audit-trail timeline for a run (FR-7): who/when/action/comments.
   * Tolerates either a bare array or a `{ data }`-style page.
   */
  getApprovalHistory(runId: string): Observable<IApprovalHistoryEntry[]> {
    return this.http
      .get<ApprovalHistoryWire[] | { data: ApprovalHistoryWire[] }>(
        `${this.runsUrl}/${runId}/approval-history`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res).map(mapApprovalHistoryEntry)));
  }

  /**
   * The approver's "Pending Approvals" queue (§8, DF-14). Hits the dedicated
   * `GET /payroll/approval/pending` endpoint, which returns ONLY the runs the
   * current approver can actually act on (scoped server-side by the approval
   * workflow — not every AwaitingApproval run, as the old `runs?status=` call did).
   *
   * The endpoint returns `PendingApprovalDto[]` (a slimmer shape than a full run):
   * `runId` maps to `IPayrollRun.id`, and `initiatedByName` IS carried by this DTO
   * (unlike `PayrollRunDto`). Tolerates either a bare array or a `{ data }`-style
   * envelope, then maps each DTO to the `IPayrollRun` the queue card renders. The
   * hand-written `IPendingApprovalDto` that used to live at the bottom of this file
   * is gone — `mapPendingApproval` binds to the generated contract instead.
   */
  listPendingApprovals(): Observable<IPayrollRun[]> {
    return this.http
      .get<PendingApprovalWire[] | { data: PendingApprovalWire[] }>(
        this.pendingUrl,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res).map(mapPendingApproval)));
  }

  /** Accept either a bare array or a `{ data }` page; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
