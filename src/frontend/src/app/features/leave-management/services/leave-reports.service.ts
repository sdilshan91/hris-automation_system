import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ReportType,
  ChartType,
  IReportQuery,
  IReportPage,
  IAnalyticsResponse,
  ILeaveSummaryMetrics,
  IExportJobResponse,
  ExportFormat,
  IReportFilters,
  IReportErrorResponse,
} from '../models/leave-reports.models';

/**
 * US-LV-012: Service for the Leave Reports & Analytics module (HR-facing).
 *
 * All requests include `withCredentials` for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). Reports
 * are tenant-isolated + role-scoped server-side (BR-1/BR-2); the FE only renders.
 *
 * `environment.apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves`.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel; see
 * the vault "Frontend (US-LV-012)" section for reconciliation):
 *   GET /api/v1/leaves/reports/{reportType}        - paginated tabular report  (FR-6)
 *   GET /api/v1/leaves/analytics/{chartType}       - chart-shaped aggregates   (FR-7)
 *   GET /api/v1/leaves/reports/summary             - dashboard summary metrics (AC cards)
 *   GET /api/v1/leaves/reports/{reportType}/export - CSV/Excel file or job id  (FR-4/AC-5)
 */
@Injectable({ providedIn: 'root' })
export class LeaveReportsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  // ─── Report data (FR-3 / FR-6) ───────────────────────────

  /** Fetch one page of a tabular report with filters + server-side sort/pagination. */
  getReport(reportType: ReportType, query: IReportQuery): Observable<IReportPage> {
    return this.http.get<IReportPage>(`${this.baseUrl}/reports/${reportType}`, {
      params: this.toParams(query),
      withCredentials: true,
    });
  }

  // ─── Analytics / charts (FR-7) ───────────────────────────

  /** Fetch chart-shaped aggregates for a chart in a report-detail view. */
  getAnalytics(chartType: ChartType, filters: IReportFilters): Observable<IAnalyticsResponse> {
    return this.http.get<IAnalyticsResponse>(`${this.baseUrl}/analytics/${chartType}`, {
      params: this.filterParams(filters),
      withCredentials: true,
    });
  }

  // ─── Dashboard summary widgets (AC cards) ────────────────

  /** Fetch the landing-page summary metrics (total utilization %, top type, absenteeism rate). */
  getSummaryMetrics(filters: IReportFilters = {}): Observable<ILeaveSummaryMetrics> {
    return this.http.get<ILeaveSummaryMetrics>(`${this.baseUrl}/reports/summary`, {
      params: this.filterParams(filters),
      withCredentials: true,
    });
  }

  // ─── Export (FR-4 / AC-5) ────────────────────────────────

  /**
   * Request an export. The backend either returns the file synchronously (small
   * datasets) or a `{ status:'processing', jobId }` envelope for large ones (AC-5).
   *
   * We send `Accept: application/json` and read the response as a Blob, then sniff
   * the content-type: a JSON body is the background-job envelope; anything else is
   * the file. This lets ONE call handle both paths.
   *
   * DEFER (seam only): real background-export polling / notification. When the
   * backend returns `status:'processing'` the component shows a "you'll be
   * notified" state and does NOT poll. TODO(export-polling).
   */
  export(
    reportType: ReportType,
    format: ExportFormat,
    filters: IReportFilters,
  ): Observable<{ blob: Blob; contentType: string; filename: string }> {
    const params = this.filterParams(filters).set('format', format);
    return new Observable((observer) => {
      const sub = this.http
        .get(`${this.baseUrl}/reports/${reportType}/export`, {
          params,
          withCredentials: true,
          observe: 'response',
          responseType: 'blob',
        })
        .subscribe({
          next: (res) => {
            const contentType = res.headers.get('Content-Type') ?? '';
            const filename = LeaveReportsService.filenameFromDisposition(
              res.headers.get('Content-Disposition'),
              `${reportType}.${format}`,
            );
            observer.next({ blob: res.body ?? new Blob(), contentType, filename });
            observer.complete();
          },
          error: (err) => observer.error(err),
        });
      return () => sub.unsubscribe();
    });
  }

  /**
   * Parse a Blob that is actually a JSON background-job envelope (AC-5). Returns
   * null when the blob is the file itself. The component calls this to decide
   * whether to download or show the "processing" state.
   */
  static async readJobEnvelope(blob: Blob, contentType: string): Promise<IExportJobResponse | null> {
    if (!contentType.includes('application/json')) {
      return null;
    }
    try {
      const text = await blob.text();
      return JSON.parse(text) as IExportJobResponse;
    } catch {
      return null;
    }
  }

  // ─── Helpers ─────────────────────────────────────────────

  private toParams(query: IReportQuery): HttpParams {
    let p = this.filterParams(query);
    p = p.set('page', String(query.page)).set('pageSize', String(query.pageSize));
    if (query.sortBy) {
      p = p.set('sortBy', query.sortBy);
    }
    if (query.sortDir) {
      p = p.set('sortDir', query.sortDir);
    }
    return p;
  }

  private filterParams(f: IReportFilters): HttpParams {
    let p = new HttpParams();
    if (f.from) {
      p = p.set('from', f.from);
    }
    if (f.to) {
      p = p.set('to', f.to);
    }
    if (f.departmentId) {
      p = p.set('departmentId', f.departmentId);
    }
    if (f.jobLevel) {
      p = p.set('jobLevel', f.jobLevel);
    }
    if (f.employmentType) {
      p = p.set('employmentType', f.employmentType);
    }
    if (f.leaveTypeId) {
      p = p.set('leaveTypeId', f.leaveTypeId);
    }
    if (f.search) {
      p = p.set('search', f.search);
    }
    return p;
  }

  /** Extract a filename from a Content-Disposition header, falling back to a default. */
  static filenameFromDisposition(disposition: string | null, fallback: string): string {
    if (!disposition) {
      return fallback;
    }
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return match ? decodeURIComponent(match[1]) : fallback;
  }

  /** Parse an error response into a typed report error. */
  static parseError(err: HttpErrorResponse): IReportErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IReportErrorResponse;
    }
    return null;
  }

  static parseErrorMessage(err: HttpErrorResponse): string {
    return LeaveReportsService.parseError(err)?.message ?? 'Failed to load the report.';
  }
}
