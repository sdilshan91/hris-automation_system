import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IExportRecord,
  IInitiateExportRequest,
  IInitiateExportResponse,
  ExportRequestWire,
  ExportInitiatedWire,
  mapExportRecord,
  mapExportInitiated,
} from '../models/data-export.models';

/**
 * US-ADM-010: on-demand tenant data-export service.
 *
 * Serves BOTH personas off one service by parameterizing every method with an
 * optional `tenantId`:
 *  - omit it  → Tenant-Admin path  `/api/v1/tenant/data-exports...`  (implicit
 *    tenant via ITenantContext, AC-1). Built from `environment.apiBaseUrl`
 *    verbatim (already ends in `/v1`) + `/tenant/data-exports`, matching the
 *    US-ADM-010 backend `DataExportController` route.
 *  - pass it  → System-Admin path  `/api/v1/system/tenants/{id}/data-exports...`
 *    (AC-6). This lives UNDER the `/v1/system` root (same root as US-ADM-003
 *    impersonation and US-ADM-009 plan-overrides), so we use `apiBaseUrl`
 *    verbatim + `/system/tenants/{id}/data-exports` — NOT the `/api/admin`
 *    namespace.
 *
 * ENVELOPE: the global apiEnvelopeInterceptor strips the `ApiResponse<T>`
 * wrapper, so JSON methods consume BARE payloads (matching the rest of the FE).
 * The download uses `responseType: 'blob'` + `observe: 'response'`; the caller
 * reads the `Content-Disposition` filename. All requests carry `withCredentials`
 * (httpOnly cookie auth) + the tenantInterceptor header.
 *
 * Error contract surfaced to callers (propagated, handled in the component):
 *  - 409 / 400  rate-limit ("Monthly export limit reached.", BR-5) or in-progress
 *    ("An export is already in progress.", FR-9)
 *  - 403 / 400  suspended tenant for a Tenant Admin (BR-2)
 *  - 410 / 404  download of an expired/deleted bundle (AC-3)
 */
@Injectable({ providedIn: 'root' })
export class DataExportService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiBaseUrl;

  /**
   * Resolve the export base for the given persona.
   *  - `tenantId` omitted → Tenant-Admin base (`/tenant/data-exports`).
   *  - `tenantId` present → System-Admin base (`/system/tenants/{id}/data-exports`).
   */
  private baseFor(tenantId?: string): string {
    return tenantId
      ? `${this.apiBase}/system/tenants/${tenantId}/data-exports`
      : `${this.apiBase}/tenant/data-exports`;
  }

  /**
   * Initiate an export (AC-1 / AC-6). Enqueues the Hangfire bundle job and
   * returns the new export id + status. Rate-limit (BR-5), in-progress (FR-9),
   * and suspended-tenant (BR-2) errors propagate as HTTP errors.
   */
  initiate(
    request: IInitiateExportRequest,
    tenantId?: string
  ): Observable<IInitiateExportResponse> {
    return this.http
      .post<ExportInitiatedWire>(this.baseFor(tenantId), request, { withCredentials: true })
      .pipe(map(mapExportInitiated));
  }

  /** Recent exports for the tenant, reverse-chronological (§8 history list). */
  getHistory(tenantId?: string): Observable<IExportRecord[]> {
    return this.http
      .get<ExportRequestWire[]>(this.baseFor(tenantId), { withCredentials: true })
      .pipe(map((rows) => (rows ?? []).map(mapExportRecord)));
  }

  /** Current status of a single export (drives the poll loop + progress, FR-9). */
  getStatus(id: string, tenantId?: string): Observable<IExportRecord> {
    return this.http
      .get<ExportRequestWire>(`${this.baseFor(tenantId)}/${id}`, { withCredentials: true })
      .pipe(map(mapExportRecord));
  }

  /**
   * Download the generated bundle (AC-3). Returns the full response so the caller
   * reads the `Content-Disposition` filename. Bypasses the JSON envelope
   * (responseType: 'blob'). A 410/404 (expired/deleted) propagates for the
   * component to surface.
   */
  download(id: string, tenantId?: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseFor(tenantId)}/${id}/download`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }
}
