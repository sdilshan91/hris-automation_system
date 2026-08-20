import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAccrualOverCreditExposureReport,
  IAccrualExposureExport,
  AccrualExposureExportFormat,
  AccrualExposureRowWire,
  mapAccrualExposureRow,
} from '../models/accrual-over-credit-exposure.models';

/**
 * BUG-291: Accrual over-credit exposure report (READ-ONLY remediation tooling).
 *
 * Consumes the existing backend endpoint on the leave-entitlements controller:
 *   GET /api/v1/tenant/leave-entitlements/accrual-over-credit-exposure
 *       ?asOfDate=YYYY-MM-DD[&format=csv|xlsx]
 *   Permission: Leave.ConfigurePolicy
 *
 * All requests carry `withCredentials` (httpOnly cookie auth) and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The JSON
 * envelope is unwrapped to the bare report by the global apiEnvelopeInterceptor;
 * the file (csv/xlsx) response passes through that interceptor untouched.
 */
@Injectable({ providedIn: 'root' })
export class AccrualOverCreditExposureService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements`;
  private readonly resource = `${this.baseUrl}/accrual-over-credit-exposure`;

  /**
   * Fetch the exposure report as of the given date. `asOfDate` must be an
   * ISO calendar date (YYYY-MM-DD) — the backend binds it as a DateOnly.
   */
  getExposure(asOfDate: string): Observable<IAccrualOverCreditExposureReport> {
    const params = new HttpParams().set('asOfDate', asOfDate);
    // The per-row substance is typed to the generated contract (`AccrualExposureRowWire`) and mapped
    // explicitly. The report envelope has no generated DTO (see the mapper's note — the endpoint's 200 is
    // `content?: never`), so its two scalars are read defensively.
    return this.http
      .get<{ asOfDate?: string; leaveYear?: number; rows?: AccrualExposureRowWire[] }>(
        this.resource,
        { params, withCredentials: true },
      )
      .pipe(
        map((r) => ({
          asOfDate: r.asOfDate ?? '',
          leaveYear: r.leaveYear ?? 0,
          rows: (r.rows ?? []).map(mapAccrualExposureRow),
        })),
      );
  }

  /**
   * Download the same affected population as a server-generated spreadsheet
   * (csv/xlsx) for Finance to work case-by-case. Reads the response as a Blob and
   * resolves the filename from the Content-Disposition header (falling back to a
   * constructed name).
   */
  exportExposure(
    asOfDate: string,
    format: AccrualExposureExportFormat,
  ): Observable<IAccrualExposureExport> {
    const params = new HttpParams().set('asOfDate', asOfDate).set('format', format);
    return this.http
      .get(this.resource, {
        params,
        withCredentials: true,
        observe: 'response',
        responseType: 'blob',
      })
      .pipe(
        map((res) => ({
          blob: res.body ?? new Blob(),
          filename: AccrualOverCreditExposureService.filenameFromDisposition(
            res.headers.get('Content-Disposition'),
            `accrual-over-credit-exposure-${asOfDate}.${format}`,
          ),
        })),
      );
  }

  /** Extract a filename from a Content-Disposition header, falling back to a default. */
  static filenameFromDisposition(disposition: string | null, fallback: string): string {
    if (!disposition) {
      return fallback;
    }
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return match ? decodeURIComponent(match[1]) : fallback;
  }

  /** Parse an error response into a human-readable message. */
  static parseError(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'Failed to load the accrual over-credit exposure report.';
  }
}
