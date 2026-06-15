import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { map, switchMap, takeWhile } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IPayslip,
  IPayslipGenerationStatus,
} from '../models/payslip.models';

/**
 * US-PAY-004: Service for individual payslips within a payroll run — list payslips
 * (§8 table), enqueue PDF generation / regeneration (AC-1/AC-5), poll generation
 * status for the progress bar (§8), and binary downloads of a single PDF (AC-3) or
 * a bulk ZIP (AC-3).
 *
 * Sibling to PayrollRunService / EmployeeSalaryService; the payslip route strings
 * live here ONLY, so a backend contract change is a one-file fix. All requests use
 * withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend stamps tenant_id,
 * enforces RLS, and validates blob paths (AC-4, NFR-6).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the JSON methods consume BARE payloads. Statuses arrive as
 * PascalCase STRINGS (US-PLT-003) — see payslip.models.ts.
 *
 * Binary downloads use `responseType: 'blob'` + `observe: 'response'` so the caller
 * can read the `Content-Disposition` filename (BR-5: `{EmployeeNo}_{Month}_{Year}.pdf`)
 * and trigger an anchor-click download; the JSON envelope does not apply to them.
 */
@Injectable({ providedIn: 'root' })
export class PayslipService {
  private readonly http = inject(HttpClient);
  private readonly runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  /** Poll interval (ms) for the generation status bar (§8). */
  static readonly POLL_INTERVAL_MS = 2000;

  /**
   * All payslips for a run, for the Notion-style table (§8). Tolerates either a
   * bare array or a `{ data }`-style page so a backend pagination choice doesn't
   * break the list.
   */
  listPayslips(runId: string): Observable<IPayslip[]> {
    return this.http
      .get<IPayslip[] | { data: IPayslip[] }>(
        `${this.runsUrl}/${runId}/payslips`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /**
   * Enqueue PDF generation for every payslip in the run (AC-1). The backend
   * enqueues a Hangfire job and returns the initial generation status; the FE then
   * polls `streamGenerationStatus` to drive the progress bar.
   */
  generatePayslips(runId: string): Observable<IPayslipGenerationStatus> {
    return this.http.post<IPayslipGenerationStatus>(
      `${this.runsUrl}/${runId}/payslips/generate`,
      {},
      { withCredentials: true },
    );
  }

  /**
   * Regenerate (overwrite) all payslip PDFs using the current template (AC-5).
   * Only valid on a non-finalized run; the backend enforces that rule.
   */
  regeneratePayslips(runId: string): Observable<IPayslipGenerationStatus> {
    return this.http.post<IPayslipGenerationStatus>(
      `${this.runsUrl}/${runId}/payslips/regenerate`,
      {},
      { withCredentials: true },
    );
  }

  /** A single point-in-time generation-status snapshot for the run (§8 progress bar). */
  getGenerationStatus(runId: string): Observable<IPayslipGenerationStatus> {
    return this.http.get<IPayslipGenerationStatus>(
      `${this.runsUrl}/${runId}/payslips/status`,
      { withCredentials: true },
    );
  }

  /**
   * Stream generation status for the progress bar (§8: "4,850 / 5,000 generated").
   * POLLING implementation: re-queries the status endpoint every ~2s and stops once
   * the run is no longer generating (`isGenerating === false`), emitting that final
   * snapshot. The component completes and refetches the payslip list for fresh
   * per-row PDF statuses.
   *
   * SIGNALR SWAP POINT: this is the ONE isolated method to replace if/when the
   * backend exposes a SignalR generation hub — connect to the hub here and emit the
   * same `IPayslipGenerationStatus` shape; nothing else in the feature changes.
   */
  streamGenerationStatus(
    runId: string,
  ): Observable<IPayslipGenerationStatus> {
    return timer(0, PayslipService.POLL_INTERVAL_MS).pipe(
      switchMap(() => this.getGenerationStatus(runId)),
      // Keep emitting while generating; emit the first terminal snapshot, then complete.
      takeWhile((s) => s.isGenerating, true),
    );
  }

  /**
   * Download a single employee's payslip PDF as a blob (AC-3). Keyed by (runId,
   * employeeId) to match the tenant-scoped backend route. Returns the full response
   * so the caller can read the `Content-Disposition` filename (BR-5). The PDF is also
   * used for the in-browser preview modal (§8) via a blob URL before download.
   */
  downloadPayslip(runId: string, employeeId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(
      `${this.runsUrl}/${runId}/payslips/${employeeId}/download`,
      {
        withCredentials: true,
        responseType: 'blob',
        observe: 'response',
      },
    );
  }

  /**
   * Download all payslips for the run as a single ZIP archive (AC-3, §8 "Download
   * All"). Desktop-only in the UI (§8); the bulk download is hidden/disabled on
   * mobile. Returns the full response so the caller derives the ZIP filename from
   * `Content-Disposition`.
   */
  downloadZip(runId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(
      `${this.runsUrl}/${runId}/payslips/download-zip`,
      {
        withCredentials: true,
        responseType: 'blob',
        observe: 'response',
      },
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
