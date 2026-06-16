import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { IPayrollRun } from '../models/payroll-run.models';
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
 *   GET  /payroll/runs?status=AwaitingApproval - pending-approvals queue (§8)
 */
@Injectable({ providedIn: 'root' })
export class PayrollApprovalService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

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
   * Runs currently Awaiting Approval — the approver's "Pending Approvals" queue
   * (§8). Server-filtered by status so the FE only sees runs it can act on.
   * Tolerates either a bare array or a `{ data }`-style page.
   */
  listPendingApprovals(): Observable<IPayrollRun[]> {
    return this.http
      .get<IPayrollRun[] | { data: IPayrollRun[] }>(this.runsUrl, {
        params: { status: 'AwaitingApproval' },
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res)));
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
