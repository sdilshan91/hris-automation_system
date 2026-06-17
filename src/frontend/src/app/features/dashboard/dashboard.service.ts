import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IDashboardResponse } from './dashboard.models';

/**
 * US-RPT-005: Dashboard with KPI Widgets service.
 *
 * TENANT-scoped endpoint under `/api/v1/dashboard...`. Base derived from
 * `environment.apiBaseUrl` verbatim (already ends in `/v1`) + `/dashboard`.
 * Tenant + role isolation is enforced server-side via ITenantContext + EF
 * global filters and the user's role (AC-5 / FR-8); the FE just carries the
 * auth cookie + the X-Tenant-Subdomain header (added by the tenantInterceptor).
 * Bare payloads (the global ApiResponse unwrap interceptor strips the envelope).
 *
 * Backend contract:
 *   GET /api/v1/dashboard/widgets?refresh={bool} — the role-appropriate widget
 *     set (FR-3). `refresh=true` bypasses the Redis cache (FR-4/FR-9).
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/dashboard`;

  /**
   * Fetch the signed-in user's role-based dashboard widgets (FR-3).
   * @param refresh when true, bypass the server Redis cache (FR-9 on-demand).
   */
  getWidgets(refresh = false): Observable<IDashboardResponse> {
    const params = new HttpParams().set('refresh', refresh);
    return this.http.get<IDashboardResponse>(`${this.baseUrl}/widgets`, {
      params,
      withCredentials: true,
    });
  }

  /** Convenience: force a cache-bypassing fetch (FR-9 Refresh button). */
  refresh(): Observable<IDashboardResponse> {
    return this.getWidgets(true);
  }
}
