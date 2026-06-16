import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ExportFormat,
  IDashboardFilters,
  IDashboardOverview,
  IDepartmentDrilldown,
  ITrendResponse,
} from '../models/dashboard.models';

/**
 * US-PRF-007: service for the Performance Dashboard & Analytics experience —
 * overview widgets, multi-cycle trend, department drill-down and exports.
 *
 * Sibling to PerformanceGoalService (US-PRF-001), SelfAssessmentService (US-PRF-002),
 * ManagerReviewService (US-PRF-003), CycleService (US-PRF-004), Feedback360Service
 * (US-PRF-005) and ReviewSignoffService (US-PRF-006).
 *
 * The route strings + the filter→query-param mapping live HERE ONLY, so a backend
 * contract change is a one-file fix. All requests use withCredentials (httpOnly
 * cookie auth) and are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header). The backend resolves the tenant + acting user from the session and enforces
 * the user's SCOPE (BR-1/AC-5: `Performance.Read.All` → org-wide, `Performance.Read.Team`
 * → the manager's direct reports). The FE sends no tenant id and does NOT decide scope —
 * the overview payload carries the authoritative `scope`.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips `{ data }`,
 * so the JSON methods consume BARE payloads. Enums arrive as PascalCase STRINGS
 * (US-PLT-003). Exports stream as a Blob (Content-Disposition filename), NOT unwrapped.
 */
@Injectable({ providedIn: 'root' })
export class PerformanceDashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/performance/dashboard`;

  /**
   * Every overview widget in one call (AC-1/AC-2/NFR-1): scope, filter option lists,
   * completion rate, average score, score-distribution histogram, department averages,
   * top/bottom (or team-ranking) performers and cycle progress metrics. Re-fetched
   * whenever the filter selection changes.
   */
  getOverview(filters: IDashboardFilters): Observable<IDashboardOverview> {
    return this.http.get<IDashboardOverview>(`${this.baseUrl}/overview`, {
      params: this.toParams(filters),
      withCredentials: true,
    });
  }

  /**
   * Multi-cycle trend (AC-3/FR-7): average score per selected cycle for the org/team
   * aggregate, plus optional per-department overlay series. The selected cycles +
   * departments come from the filter selection.
   */
  getTrend(filters: IDashboardFilters): Observable<ITrendResponse> {
    return this.http.get<ITrendResponse>(`${this.baseUrl}/trend`, {
      params: this.toParams(filters),
      withCredentials: true,
    });
  }

  /**
   * Department drill-down (FR-5): the department's employees with individual scores +
   * the breadcrumb labels. Reached by clicking a department bar. The active cycle
   * filter (first selected, if any) is passed through so the scores match the chart.
   */
  getDepartmentDrilldown(
    departmentId: string,
    cycleId?: string,
  ): Observable<IDepartmentDrilldown> {
    let params = new HttpParams();
    if (cycleId) {
      params = params.set('cycleId', cycleId);
    }
    return this.http.get<IDepartmentDrilldown>(
      `${this.baseUrl}/departments/${departmentId}`,
      { params, withCredentials: true },
    );
  }

  /**
   * FR-8/AC-4: download the server-generated export in the requested format. The
   * backend owns CSV/XLSX/PDF generation + tenant branding on the PDF; the FE just
   * triggers the download. Returns the full HttpResponse so the caller can read the
   * Content-Disposition filename. The current filter selection scopes the export to
   * what is on screen.
   */
  export(
    format: ExportFormat,
    filters: IDashboardFilters,
  ): Observable<HttpResponse<Blob>> {
    const params = this.toParams(filters).set('format', format);
    return this.http.get(`${this.baseUrl}/export`, {
      params,
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /**
   * Map the multi-select filter selection to repeated query params (e.g.
   * `?cycleId=a&cycleId=b&departmentId=…`). Empty facets are omitted. Kept in the
   * service so the wire format is a single change point.
   */
  private toParams(filters: IDashboardFilters): HttpParams {
    let params = new HttpParams();
    params = this.appendAll(params, 'cycleId', filters.cycleIds);
    params = this.appendAll(params, 'departmentId', filters.departmentIds);
    params = this.appendAll(params, 'grade', filters.grades);
    params = this.appendAll(params, 'employmentType', filters.employmentTypes);
    params = this.appendAll(params, 'location', filters.locations);
    return params;
  }

  private appendAll(
    params: HttpParams,
    key: string,
    values: string[],
  ): HttpParams {
    let next = params;
    for (const v of values) {
      next = next.append(key, v);
    }
    return next;
  }
}
