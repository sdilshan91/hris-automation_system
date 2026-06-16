import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { switchMap, takeWhile } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IPayslipDistributionAccepted,
  IPayslipDistributionStatus,
  IResendRequest,
  ISendPayslipsRequest,
} from '../models/payslip-email.models';

/**
 * US-PAY-011: Service for BULK PAYSLIP EMAIL DISTRIBUTION within a payroll run —
 * enqueue the distribution job (AC-1), poll its status for the progress bar +
 * summary (§8), and selectively re-send to failed/specific employees (FR-4).
 *
 * Sibling to PayslipService / PayrollRunService; the distribution route strings
 * live here ONLY, so a backend contract change is a one-file fix. All requests use
 * withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend stamps tenant_id,
 * enforces RLS, and guarantees no cross-employee/cross-tenant leakage (AC-5).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Delivery statuses
 * arrive as PascalCase STRINGS (US-PLT-003) — see payslip-email.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class PayslipEmailService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  /** Poll interval (ms) for the distribution progress bar (§8). */
  static readonly POLL_INTERVAL_MS = 2000;

  /**
   * Enqueue bulk payslip email distribution for the run (AC-1). The backend enqueues
   * the distribution job and returns the 202 ACCEPTED payload
   * (`IPayslipDistributionAccepted`, NOT the status) — the FE then starts polling
   * `streamDistributionStatus` to drive the progress bar + summary.
   *
   * `confirm` (FR-7/BR-5): when payslips were already sent for this run, the backend
   * rejects the request with 409 `resend_requires_confirmation` unless `confirm: true`
   * — the FE only sets it after the HR officer explicitly acknowledges the
   * duplicate-send confirmation dialog (retry with `confirm: true`).
   */
  sendPayslips(
    runId: string,
    confirm: boolean,
  ): Observable<IPayslipDistributionAccepted> {
    const body: ISendPayslipsRequest = { confirm };
    return this.http.post<IPayslipDistributionAccepted>(
      `${this.runsUrl}/${runId}/payslips/send-emails`,
      body,
      { withCredentials: true },
    );
  }

  /** A single point-in-time distribution-status snapshot (§8 progress + summary). */
  getDistributionStatus(
    runId: string,
  ): Observable<IPayslipDistributionStatus> {
    return this.http.get<IPayslipDistributionStatus>(
      `${this.runsUrl}/${runId}/payslips/distribution-summary`,
      { withCredentials: true },
    );
  }

  /**
   * Re-send to every FAILED delivery in the run (FR-4 "Re-send All Failed"). Returns
   * the 202 ACCEPTED payload; the re-queued failures move back to Queued and the FE
   * resumes polling the distribution summary.
   */
  resendAllFailed(runId: string): Observable<IPayslipDistributionAccepted> {
    const body: IResendRequest = { onlyFailed: true };
    return this.http.post<IPayslipDistributionAccepted>(
      `${this.runsUrl}/${runId}/payslips/resend-emails`,
      body,
      { withCredentials: true },
    );
  }

  /**
   * Re-send to a specific set of employees (FR-4 per-employee re-send — e.g. a
   * single failed delivery, or newly-added email addresses). Returns the 202
   * ACCEPTED payload.
   */
  resendSelective(
    runId: string,
    employeeIds: string[],
  ): Observable<IPayslipDistributionAccepted> {
    const body: IResendRequest = { employeeIds };
    return this.http.post<IPayslipDistributionAccepted>(
      `${this.runsUrl}/${runId}/payslips/resend-emails`,
      body,
      { withCredentials: true },
    );
  }

  /**
   * Stream distribution status for the progress bar (§8: "sent / total"). POLLING
   * implementation: re-queries the `distribution-summary` endpoint every ~2s and
   * stops once the run is no longer sending (`isSending === false`), emitting that
   * final snapshot. The component then has the completed summary (Sent / Failed /
   * Skipped, BR-6).
   *
   * SIGNALR SWAP POINT: this is the ONE isolated method to replace if/when the
   * backend exposes a SignalR distribution hub (the story's §8 real-time progress) —
   * connect to the hub here and emit the same `IPayslipDistributionStatus` shape;
   * nothing else in the feature changes.
   */
  streamDistributionStatus(
    runId: string,
  ): Observable<IPayslipDistributionStatus> {
    return timer(0, PayslipEmailService.POLL_INTERVAL_MS).pipe(
      switchMap(() => this.getDistributionStatus(runId)),
      // Keep emitting while sending; emit the first terminal snapshot, then complete.
      takeWhile((s) => s.isSending, true),
    );
  }
}
