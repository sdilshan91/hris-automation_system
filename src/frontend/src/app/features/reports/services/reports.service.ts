import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IReportCatalogItem,
  IReportCatalogServerItem,
  IReportFilters,
  IReportResult,
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
}
