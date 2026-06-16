import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IPlatformHealth,
  ITenantMonitoringDetail,
  ITenantUsageFilters,
  ITenantUsageSummary,
} from '../models/monitoring.models';

/**
 * US-ADM-002: System Admin Console platform-monitoring service.
 *
 * Codes to the System Admin backend contract rooted at `/api/admin/monitoring`
 * (the platform/system context) — NOT the tenant-scoped `/api/v1` namespace. We
 * derive the `/api` root by stripping the trailing `/v1` from
 * `environment.apiBaseUrl`, matching the sibling US-ADM-001 service.
 *
 * Endpoints:
 *   GET /api/admin/monitoring/health          platform health rollup (AC-1)
 *   GET /api/admin/monitoring/tenants         per-tenant usage summaries (AC-2/3)
 *   GET /api/admin/monitoring/tenants/{id}    tenant detail (AC-4)
 *
 * Envelope: the global apiEnvelopeInterceptor strips the `ApiResponse<T>`
 * wrapper, so these methods consume BARE payloads. All requests use
 * withCredentials (httpOnly cookie auth).
 */
@Injectable({ providedIn: 'root' })
export class PlatformMonitoringService {
  private readonly http = inject(HttpClient);

  /** `/api` root (strip the tenant `/v1` suffix — admin console is system-scoped). */
  private readonly monitoringUrl = `${environment.apiBaseUrl.replace(
    /\/v1$/,
    '',
  )}/admin/monitoring`;

  /** AC-1/FR-1/FR-6: platform health rollup. */
  getHealth(): Observable<IPlatformHealth> {
    return this.http.get<IPlatformHealth>(`${this.monitoringUrl}/health`, {
      withCredentials: true,
    });
  }

  /** AC-2/FR-2/FR-4: per-tenant usage summaries, optionally filtered. */
  getTenantUsage(
    filters: ITenantUsageFilters = {},
  ): Observable<ITenantUsageSummary[]> {
    let params = new HttpParams();
    if (filters.search) params = params.set('search', filters.search);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.plan) params = params.set('plan', filters.plan);
    if (filters.region) params = params.set('region', filters.region);
    if (filters.createdFrom) params = params.set('createdFrom', filters.createdFrom);
    if (filters.createdTo) params = params.set('createdTo', filters.createdTo);

    return this.http.get<ITenantUsageSummary[]>(`${this.monitoringUrl}/tenants`, {
      params,
      withCredentials: true,
    });
  }

  /** AC-4: a single tenant's monitoring detail. */
  getTenantDetail(tenantId: string): Observable<ITenantMonitoringDetail> {
    return this.http.get<ITenantMonitoringDetail>(
      `${this.monitoringUrl}/tenants/${tenantId}`,
      { withCredentials: true },
    );
  }
}
