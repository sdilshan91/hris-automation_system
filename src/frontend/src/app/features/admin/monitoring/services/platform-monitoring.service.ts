import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IPlatformHealth,
  ITenantMonitoringDetail,
  ITenantUsageDashboard,
  ITenantUsageFilters,
} from '../models/monitoring.models';

/**
 * US-ADM-002: System Admin Console platform-monitoring service.
 *
 * Codes to the System Admin backend contract (AdminMonitoringController), rooted
 * at `/api/v1/system/monitoring` — the `/v1/system` namespace. We append to
 * `environment.apiBaseUrl` (`…/api/v1`) verbatim.
 *
 * Endpoints (must match AdminMonitoringController exactly):
 *   GET /api/v1/system/monitoring/health         platform health rollup (AC-1)
 *   GET /api/v1/system/monitoring/tenant-usage   per-tenant usage summaries (AC-2/3)
 *   GET /api/v1/system/monitoring/tenants/{id}   tenant detail (AC-4)
 *
 * Envelope: the global apiEnvelopeInterceptor strips the `ApiResponse<T>`
 * wrapper, so these methods consume BARE payloads. All requests use
 * withCredentials (httpOnly cookie auth).
 */
@Injectable({ providedIn: 'root' })
export class PlatformMonitoringService {
  private readonly http = inject(HttpClient);

  /** `/api/v1/system/monitoring` — the system-admin monitoring namespace. */
  private readonly monitoringUrl = `${environment.apiBaseUrl}/system/monitoring`;

  /** AC-1/FR-1/FR-6: platform health rollup. */
  getHealth(): Observable<IPlatformHealth> {
    return this.http.get<IPlatformHealth>(`${this.monitoringUrl}/health`, {
      withCredentials: true,
    });
  }

  /**
   * AC-2/FR-2/FR-4: the tenant-usage dashboard, optionally filtered. The BE wraps
   * the rows in a `TenantUsageDashboardDto` object (`{ tenants, quotaBreachQueue,
   * attentionRequiredQueue, attentionQueueStatus, generatedAtUtc }`) — NOT a bare
   * array. Read `.tenants` for the table.
   */
  getTenantUsage(
    filters: ITenantUsageFilters = {},
  ): Observable<ITenantUsageDashboard> {
    let params = new HttpParams();
    if (filters.search) params = params.set('search', filters.search);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.plan) params = params.set('plan', filters.plan);
    if (filters.region) params = params.set('region', filters.region);
    if (filters.createdFrom) params = params.set('createdFrom', filters.createdFrom);
    if (filters.createdTo) params = params.set('createdTo', filters.createdTo);

    return this.http.get<ITenantUsageDashboard>(`${this.monitoringUrl}/tenant-usage`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * AC-4: a single tenant's monitoring detail.
   *
   * The BE serializes the tenant lifecycle status as a PascalCase enum string
   * ("Active", "Suspended", "Terminating", …). The detail view's lifecycle
   * quick-action gating (canSuspend/canTerminate/… in the lifecycle module) and
   * its `status === 'terminated'` template checks are case-sensitive against
   * LOWERCASE values. Normalise `status` to lowercase here at the service
   * boundary so those gates keep working — without touching the shared lifecycle
   * contract. Every other field is passed through verbatim.
   */
  getTenantDetail(tenantId: string): Observable<ITenantMonitoringDetail> {
    return this.http
      .get<ITenantMonitoringDetail>(`${this.monitoringUrl}/tenants/${tenantId}`, {
        withCredentials: true,
      })
      .pipe(
        map((detail) => ({
          ...detail,
          status: (detail.status ?? '').toLowerCase(),
        })),
      );
  }
}
