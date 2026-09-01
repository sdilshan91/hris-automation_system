import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  ISuspendTenantRequest,
  ITerminateTenantRequest,
  ITenantLifecycleEvent,
  ITenantLifecycleResult,
  TenantLifecycleResultWire,
  TenantLifecycleEventWire,
  mapTenantLifecycleResult,
  mapTenantLifecycleEvent,
} from '../models/lifecycle.models';

/**
 * US-ADM-004: System Admin tenant lifecycle service.
 *
 * Codes to the backend contract rooted at
 * `/api/v1/system/tenants/{tenantId}/lifecycle` — UNDER the `/v1/system`
 * namespace (like US-ADM-003 impersonation), so we use `environment.apiBaseUrl`
 * (`…/api/v1`) verbatim and append `/system/tenants/...`. This is DIFFERENT from
 * the US-ADM-002 monitoring service, which strips `/v1` for `/api/admin`.
 *
 * Endpoints:
 *   POST .../lifecycle/suspend     { reason }              suspend (AC-1)
 *   POST .../lifecycle/terminate   { reason, graceDays }   terminate (AC-3)
 *   POST .../lifecycle/reactivate  (no body)               reactivate (AC-5)
 *   POST .../lifecycle/restore     (no body)               restore (AC-6)
 *   GET  .../lifecycle/history                             history timeline
 *
 * Envelope: the global apiEnvelopeInterceptor strips `ApiResponse<T>`, so these
 * methods consume BARE payloads. All requests use withCredentials (httpOnly
 * cookie auth), matching the sibling admin services.
 *
 * D1: every response is now normalized through `mapTenantLifecycle*`. The wire's
 * `status` is a PascalCase `TenantStatus` name; the FE's gates compare against
 * snake_case tokens, so an un-normalized result written back into the detail view
 * disabled all four lifecycle quick-actions until the next refetch.
 */
@Injectable({ providedIn: 'root' })
export class TenantLifecycleService {
  private readonly http = inject(HttpClient);

  /** `/api/v1/system/tenants` — lifecycle is rooted at the `/v1/system` namespace. */
  private readonly baseUrl = `${environment.apiBaseUrl}/system/tenants`;

  private lifecycleUrl(tenantId: string): string {
    return `${this.baseUrl}/${tenantId}/lifecycle`;
  }

  /** AC-1 / FR-1: suspend a tenant with a mandatory reason. */
  suspend(
    tenantId: string,
    request: ISuspendTenantRequest,
  ): Observable<ITenantLifecycleResult> {
    return this.http
      .post<TenantLifecycleResultWire>(
        `${this.lifecycleUrl(tenantId)}/suspend`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapTenantLifecycleResult));
  }

  /** AC-3 / FR-2: terminate a tenant with a reason and grace period. */
  terminate(
    tenantId: string,
    request: ITerminateTenantRequest,
  ): Observable<ITenantLifecycleResult> {
    return this.http
      .post<TenantLifecycleResultWire>(
        `${this.lifecycleUrl(tenantId)}/terminate`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapTenantLifecycleResult));
  }

  /** AC-5 / FR-5: reactivate a suspended tenant (no body). */
  reactivate(tenantId: string): Observable<ITenantLifecycleResult> {
    return this.http
      .post<TenantLifecycleResultWire>(
        `${this.lifecycleUrl(tenantId)}/reactivate`,
        null,
        { withCredentials: true },
      )
      .pipe(map(mapTenantLifecycleResult));
  }

  /** AC-6 / FR-6: restore a terminating tenant within its grace period (no body). */
  restore(tenantId: string): Observable<ITenantLifecycleResult> {
    return this.http
      .post<TenantLifecycleResultWire>(
        `${this.lifecycleUrl(tenantId)}/restore`,
        null,
        { withCredentials: true },
      )
      .pipe(map(mapTenantLifecycleResult));
  }

  /** Lifecycle history timeline for the tenant detail view (FR-7). */
  getHistory(tenantId: string): Observable<ITenantLifecycleEvent[]> {
    return this.http
      .get<TenantLifecycleEventWire[]>(
        `${this.lifecycleUrl(tenantId)}/history`,
        { withCredentials: true },
      )
      .pipe(map((rows) => (rows ?? []).map(mapTenantLifecycleEvent)));
  }
}
