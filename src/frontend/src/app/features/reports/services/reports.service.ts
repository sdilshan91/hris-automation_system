import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IExportHistoryItem,
  IExportRequest,
  IExportResponse,
  IReportCatalogItem,
  IReportCatalogServerItem,
  IReportFilters,
  IReportResult,
  ReportExportFormat,
  ReportType,
} from '../models/reports.models';

/**
 * US-RPT-001: Pre-Built HR Reports service.
 *
 * TENANT-scoped endpoints under `/api/v1/reports...` (sibling report endpoints
 * /leaves, /payroll, /attendance use no /tenant prefix; the backend route is
 * `api/v1/reports`). Base derived from `environment.apiBaseUrl` verbatim
 * (already ends in `/v1`) + `/reports`. Tenant isolation is enforced
 * server-side via ITenantContext + EF global filters (AC-5 / FR-7); here we
 * just carry the auth cookie + the X-Tenant-Subdomain header (added by the
 * tenantInterceptor). Bare payloads (no ApiResponse unwrap).
 *
 * Backend contract (generic ReportDto so all six report types share one viewer):
 *   GET  /api/v1/reports                  — catalog ({ type, icon }[]; the FE
 *                                           owns i18n and derives title/desc keys
 *                                           from type).
 *   POST /api/v1/reports/{type}/generate  — body IReportFilters, optional
 *                                           ?refresh=true to bypass the Redis
 *                                           cache (FR-8), returns IReportResult.
 */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/reports`;

  /**
   * AC-1 / FR-1: the catalog of pre-built report types. The server sends only
   * `{ type, icon }`; the FE owns i18n, so we derive the title/description
   * translation keys from `type` here.
   */
  getCatalog(): Observable<IReportCatalogItem[]> {
    return this.http
      .get<IReportCatalogServerItem[]>(this.baseUrl, {
        withCredentials: true,
      })
      .pipe(
        map((items) =>
          items.map((item) => ({
            type: item.type,
            icon: item.icon,
            titleKey: `reports.catalog.${item.type}.title`,
            descriptionKey: `reports.catalog.${item.type}.description`,
            category: item.category,
          }))
        )
      );
  }

  /**
   * AC-2..AC-4 / FR-2..FR-4: generate one report. `refresh=true` bypasses the
   * Redis cache and regenerates (FR-8). Filters go in the body; the generic
   * IReportResult renders through the single viewer.
   */
  generateReport(
    type: ReportType,
    filters: IReportFilters,
    refresh = false
  ): Observable<IReportResult> {
    const params = new HttpParams().set('refresh', refresh ? 'true' : 'false');
    return this.http.post<IReportResult>(
      `${this.baseUrl}/${type}/generate`,
      filters,
      { params, withCredentials: true }
    );
  }

  /**
   * US-RPT-004 AC-1..AC-4 / FR-1: trigger an export. Returns the UNIFORM JSON
   * descriptor (NOT the file) so the UI can branch on `status` with no
   * content-type sniffing: `Completed` → download now, `Queued` → toast +
   * refresh history. `includeCharts` is only sent when supplied (PDF/Excel).
   *   POST /api/v1/reports/{type}/export
   *     body { format, filters, includeCharts? } → { exportId, status, rowCount, format }
   */
  exportReport(
    type: ReportType,
    filters: IReportFilters,
    format: ReportExportFormat,
    includeCharts?: boolean
  ): Observable<IExportResponse> {
    const body: IExportRequest = { format, filters };
    if (includeCharts !== undefined) {
      body.includeCharts = includeCharts;
    }
    return this.http.post<IExportResponse>(
      `${this.baseUrl}/${type}/export`,
      body,
      { withCredentials: true }
    );
  }

  /**
   * US-RPT-004 §8: the "My Exports" history. Each item carries its own
   * `downloadReady` flag (the only gate for the Download button — see
   * {@link downloadExport}).
   *   GET /api/v1/reports/exports
   */
  listExports(): Observable<IExportHistoryItem[]> {
    return this.http.get<IExportHistoryItem[]>(`${this.baseUrl}/exports`, {
      withCredentials: true,
    });
  }

  /**
   * US-RPT-004 §8 / AC-5: download a generated export FILE. Requested as a blob
   * with the full response observed so the caller can read the
   * `Content-Disposition` filename. The signed/tenant-scoped URL + 403 isolation
   * are enforced server-side (AC-5 / NFR-3); the FE just streams the attachment.
   *   GET /api/v1/reports/exports/{exportId}/download
   */
  downloadExport(exportId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}/exports/${exportId}/download`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }
}

/**
 * US-RPT-004: parse a filename from a `Content-Disposition` header, preferring
 * the RFC 5987 `filename*=UTF-8''…` form over the plain quoted `filename=`.
 * Returns null when absent so the caller can fall back to a generated name.
 */
export function filenameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8?.[1]) {
    return decodeURIComponent(utf8[1]);
  }
  const quoted = /filename="?([^";]+)"?/i.exec(header);
  return quoted?.[1] ?? null;
}

/**
 * US-RPT-004 §8: trigger a browser download for a blob with the given filename.
 * No-op in non-DOM environments (SSR/tests without a document).
 */
export function downloadBlob(blob: Blob, filename: string): void {
  if (
    typeof document === 'undefined' ||
    typeof URL === 'undefined' ||
    !URL.createObjectURL
  ) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
