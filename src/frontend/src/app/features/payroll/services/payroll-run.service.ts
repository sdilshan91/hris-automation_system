import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { map, switchMap, takeWhile } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IInitiatePayrollRunRequest,
  IPayrollRun,
  IPayrollRunProgress,
  IPayrollRunValidation,
  IPayrollRunValidationRequest,
  PayrollRunAcceptedWire,
  PayrollRunProgressWire,
  PayrollRunWire,
  mapPayrollRun,
  mapPayrollRunAccepted,
  mapPayrollRunProgress,
} from '../models/payroll-run.models';

/**
 * US-PAY-003: Service for tenant-scoped payroll RUNS — list/get runs, the pre-run
 * validation summary, initiating a run (with an Idempotency-Key header, FR-9), and
 * polling processing progress (FR-6).
 *
 * Sibling to PayrollService / EmployeeSalaryService; the run route strings live
 * here ONLY, so a backend contract change is a one-file fix. All requests use
 * withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend stamps tenant_id +
 * audit fields and enforces RLS (AC-7).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Statuses arrive as
 * PascalCase STRINGS (US-PLT-003) — see payroll-run.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class PayrollRunService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  /** Poll interval (ms) for the in-progress view (FR-6, §8). */
  static readonly POLL_INTERVAL_MS = 2000;

  /**
   * All payroll runs for the tenant, for the table view (§8). Tolerates either a
   * bare array or a `{ data }`-style page so a backend pagination choice doesn't
   * break the list.
   */
  listRuns(): Observable<IPayrollRun[]> {
    return this.http
      .get<PayrollRunWire[] | { data: PayrollRunWire[] }>(this.runsUrl, {
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res).map(mapPayrollRun)));
  }

  /** A single payroll run (detail view + completion summary). */
  getRun(id: string): Observable<IPayrollRun> {
    return this.http
      .get<PayrollRunWire>(`${this.runsUrl}/${id}`, { withCredentials: true })
      .pipe(map(mapPayrollRun));
  }

  /**
   * Pre-run validation summary for a period (§8: "247 employees ready, 3 missing
   * salary structure"). Drives the New Run modal — `canRun` gates Submit and any
   * `blockers` (e.g. already finalized, AC-4) are shown.
   *
   * ⚠️ NOT MIGRATED — THIS ENDPOINT DOES NOT EXIST. `POST /api/v1/payroll/runs/validate`
   * is absent from both `contracts/openapi/hrm-v1.json` and `PayrollRunsController`, and
   * there is no `IPayrollRunValidation` counterpart DTO to bind to. The call is a live
   * 404, which sets `validationError` in the New Run modal and leaves `canSubmit()`
   * permanently false — i.e. a payroll run cannot be started from the UI at all.
   * Deliberately left untouched (building the endpoint is a backend change); flagged
   * OUT-OF-LANE rather than papered over with a fabricated wire type.
   */
  validateRun(
    request: IPayrollRunValidationRequest,
  ): Observable<IPayrollRunValidation> {
    return this.http.post<IPayrollRunValidation>(
      `${this.runsUrl}/validate`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Initiate a payroll run (AC-1). Sends an Idempotency-Key header (FR-9) so a
   * retry can never enqueue a duplicate run. Returns the created run (status
   * Queued); the backend responds 202 Accepted with the runId.
   */
  initiateRun(
    request: IInitiatePayrollRunRequest,
    idempotencyKey: string,
  ): Observable<IPayrollRun> {
    return this.http
      .post<PayrollRunAcceptedWire>(this.runsUrl, request, {
        withCredentials: true,
        headers: { 'Idempotency-Key': idempotencyKey },
      })
      .pipe(map(mapPayrollRunAccepted));
  }

  /**
   * ISSUE-154: Cancel a non-finalized run. The backend removes the run's slips and
   * moves it to Cancelled, returning the accepted run DTO. Server-gated by
   * Payroll.Run; a 409 carries a machine-readable `code` (run_finalized,
   * run_in_progress, run_already_cancelled) which the caller maps to a message.
   */
  cancelRun(runId: string): Observable<IPayrollRun> {
    return this.http
      .post<PayrollRunAcceptedWire>(`${this.runsUrl}/${runId}/cancel`, null, {
        withCredentials: true,
      })
      .pipe(map(mapPayrollRunAccepted));
  }

  /**
   * ISSUE-154: Re-run (reprocess) a ReviewPending run. The backend replaces the
   * run's slips and re-enqueues processing (202 Accepted). Server-gated by
   * Payroll.Run; a 409 carries a `code` (run_not_rerunnable, run_finalized,
   * run_cancelled) which the caller maps to a message. The body (if any) is not
   * relied upon — the detail view refetches the run to pick up the new state.
   */
  rerunRun(runId: string): Observable<IPayrollRun> {
    return this.http
      .post<PayrollRunAcceptedWire>(`${this.runsUrl}/${runId}/rerun`, null, {
        withCredentials: true,
      })
      .pipe(map(mapPayrollRunAccepted));
  }

  /** A single point-in-time progress snapshot (FR-6). */
  getProgress(id: string): Observable<IPayrollRunProgress> {
    return this.http
      .get<PayrollRunProgressWire>(`${this.runsUrl}/${id}/progress`, {
        withCredentials: true,
      })
      .pipe(map(mapPayrollRunProgress));
  }

  /**
   * Stream processing progress for the in-progress view (FR-6). POLLING
   * implementation: re-queries the progress endpoint every ~2s and stops once the
   * run leaves the active (Queued/Processing) states, emitting that final
   * snapshot. The component completes and refetches the run for its summary.
   *
   * SIGNALR SWAP POINT: this is the ONE isolated method to replace if/when the
   * backend exposes a SignalR progress hub instead of a polling endpoint — connect
   * to the hub here and emit the same `IPayrollRunProgress` shape; nothing else in
   * the feature changes. (See payroll-run.models.ts IPayrollRunProgress.)
   */
  streamProgress(id: string): Observable<IPayrollRunProgress> {
    return timer(0, PayrollRunService.POLL_INTERVAL_MS).pipe(
      switchMap(() => this.getProgress(id)),
      // Keep emitting while active; emit the first terminal snapshot, then complete.
      takeWhile(
        (p) => p.status === 'Queued' || p.status === 'Processing',
        true,
      ),
    );
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
