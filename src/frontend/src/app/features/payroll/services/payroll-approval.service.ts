import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { IPayrollRun, PayrollRunStatus } from '../models/payroll-run.models';
import {
  IApprovalCommentRequest,
  IApprovalHistoryEntry,
  IApprovalSummary,
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
 * ASSUMED REST contract (story brief §FR; backend building in parallel, not yet
 * pinned in the vault):
 *   POST /payroll/runs/:id/submit            - HR submits ReviewPending -> AwaitingApproval (AC-1)
 *   POST /payroll/runs/:id/approve           - approver AwaitingApproval -> Approved (AC-2)
 *   POST /payroll/runs/:id/reject  { comments } - approver -> Rejected (AC-3, reason required)
 *   POST /payroll/runs/:id/return  { comments } - approver sends back to HR (FR-9, comments required)
 *   POST /payroll/runs/:id/finalize          - HR Approved -> Finalized (AC-5)
 *   GET  /payroll/runs/:id/approval-summary   - review summary (FR-4)
 *   GET  /payroll/runs/:id/approval-history   - audit-trail timeline (FR-7)
 *   GET  /payroll/approval/pending            - pending-approvals queue (§8, DF-14)
 */
@Injectable({ providedIn: 'root' })
export class PayrollApprovalService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;
  private readonly pendingUrl = `${environment.apiBaseUrl}/payroll/approval/pending`;

  /**
   * Submit a ReviewPending run for approval (AC-1). Creates the workflow instance
   * server-side and moves the run to AwaitingApproval. Returns the updated run.
   */
  submit(runId: string): Observable<IPayrollRun> {
    return this.http.post<IPayrollRun>(
      `${this.runsUrl}/${runId}/submit-for-approval`,
      null,
      { withCredentials: true },
    );
  }

  /**
   * Approve the current step (AC-2). When all steps are complete the run becomes
   * Approved (the backend owns the sequential multi-step routing, AC-4). Returns
   * the updated run.
   */
  approve(runId: string): Observable<IPayrollRun> {
    return this.http.post<IPayrollRun>(
      `${this.runsUrl}/${runId}/approve`,
      null,
      { withCredentials: true },
    );
  }

  /**
   * Reject the run with a reason (AC-3). The reason is REQUIRED (the form enforces
   * it; the backend re-validates). Moves the run to Rejected and notifies HR.
   */
  reject(
    runId: string,
    request: IApprovalCommentRequest,
  ): Observable<IPayrollRun> {
    return this.http.post<IPayrollRun>(
      `${this.runsUrl}/${runId}/reject`,
      request,
      { withCredentials: true },
    );
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
    return this.http.post<IPayrollRun>(
      `${this.runsUrl}/${runId}/return`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Finalize an Approved run (AC-5). Locks all payslip records (FR-8) and moves the
   * run to the terminal Finalized state. Returns the updated run.
   */
  finalize(runId: string): Observable<IPayrollRun> {
    return this.http.post<IPayrollRun>(
      `${this.runsUrl}/${runId}/finalize`,
      null,
      { withCredentials: true },
    );
  }

  /**
   * The approval review summary for a run (FR-4): totals, statutory subtotal,
   * previous-month net + variance, and the exceptions list. Backs the left summary
   * card + variance comparison on the run-detail approval review layout.
   */
  getApprovalSummary(runId: string): Observable<IApprovalSummary> {
    return this.http.get<IApprovalSummary>(
      `${this.runsUrl}/${runId}/approval-summary`,
      { withCredentials: true },
    );
  }

  /**
   * The approval audit-trail timeline for a run (FR-7): who/when/action/comments.
   * Tolerates either a bare array or a `{ data }`-style page.
   */
  getApprovalHistory(runId: string): Observable<IApprovalHistoryEntry[]> {
    return this.http
      .get<IApprovalHistoryEntry[] | { data: IApprovalHistoryEntry[] }>(
        `${this.runsUrl}/${runId}/approval-history`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /**
   * The approver's "Pending Approvals" queue (§8, DF-14). Hits the dedicated
   * `GET /payroll/approval/pending` endpoint, which returns ONLY the runs the
   * current approver can actually act on (scoped server-side by the approval
   * workflow — not every AwaitingApproval run, as the old `runs?status=` call did).
   *
   * The endpoint returns `PendingApprovalDto[]` (a slimmer shape than a full run):
   * note `runId` maps to `IPayrollRun.id`, and `initiatedByName` is now populated.
   * Tolerates either a bare array or a `{ data }`-style envelope, then maps each
   * DTO to the `IPayrollRun` the pending-approvals card renders.
   */
  listPendingApprovals(): Observable<IPayrollRun[]> {
    return this.http
      .get<IPendingApprovalDto[] | { data: IPendingApprovalDto[] }>(
        this.pendingUrl,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res).map((d) => toPayrollRun(d))));
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

/**
 * DF-14: the shape returned by `GET /payroll/approval/pending`. A slimmer view of
 * a run than `IPayrollRun` — the primary key is `runId` (not `id`), and it omits
 * `skippedEmployees`/`totalDeductions`/`completedAt`. Adds the approval-step
 * position (`currentApprovalStep` / `totalApprovalSteps`) for future display.
 */
interface IPendingApprovalDto {
  runId: string;
  payMonth: number;
  payYear: number;
  status: PayrollRunStatus;
  processedEmployees: number;
  totalEmployees: number;
  totalGross: number;
  totalNet: number;
  submittedBy: string;
  initiatedByName: string | null;
  initiatedAt: string;
  currentApprovalStep: number | null;
  totalApprovalSteps: number | null;
}

/**
 * Map a `PendingApprovalDto` to the `IPayrollRun` the pending-approvals card binds
 * (id, payMonth, payYear, status, processedEmployees, totalGross, totalNet,
 * initiatedByName, initiatedAt). `runId -> id`. The fields the endpoint doesn't
 * carry are derived where exact (`totalDeductions = gross - net`,
 * `skippedEmployees = total - processed`) or defaulted (`completedAt = null`) —
 * none of these are rendered by the queue card.
 */
function toPayrollRun(d: IPendingApprovalDto): IPayrollRun {
  return {
    id: d.runId,
    payMonth: d.payMonth,
    payYear: d.payYear,
    status: d.status,
    totalEmployees: d.totalEmployees,
    processedEmployees: d.processedEmployees,
    skippedEmployees: d.totalEmployees - d.processedEmployees,
    totalGross: d.totalGross,
    totalDeductions: d.totalGross - d.totalNet,
    totalNet: d.totalNet,
    initiatedByName: d.initiatedByName,
    initiatedAt: d.initiatedAt,
    completedAt: null,
  };
}
