import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IPlanSummary,
  IPlanDetail,
  IPlanUpsert,
  IPlanLimitOverride,
} from '../models/plan.models';

/**
 * US-ADM-009: System Admin Console subscription-plan service.
 *
 * Codes to the System Admin backend contract rooted at `/api/v1/system/plans`
 * (the platform/system context, same root as the US-ADM-003 impersonation
 * service) — so we use `environment.apiBaseUrl` (`…/api/v1`) verbatim and append
 * `/system/plans`. (NOT the `/api/admin` root that US-ADM-002 monitoring strips
 * `/v1` for — admin endpoints differ per story.)
 *
 * Endpoints:
 *   GET    /system/plans                                  list (AC-1)
 *   GET    /system/plans/{id}                             full plan (AC-2)
 *   POST   /system/plans                                  create (AC-2)
 *   PUT    /system/plans/{id}                             update (AC-3; code immutable)
 *   POST   /system/plans/{id}/archive                     archive (AC-4)
 *   DELETE /system/plans/{id}                             delete (may 409 if referenced, FR-7)
 *   GET    /system/tenants/{tenantId}/plan-overrides      list overrides (AC-5)
 *   PUT    /system/tenants/{tenantId}/plan-overrides      replace overrides (AC-5)
 *   DELETE /system/tenants/{tenantId}/plan-overrides/{key} remove one override
 *
 * Envelope: the global apiEnvelopeInterceptor strips `ApiResponse<T>`, so these
 * methods consume BARE payloads. All requests use withCredentials (httpOnly
 * cookie auth), matching the sibling admin services.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionPlanService {
  private readonly http = inject(HttpClient);

  /** `/api/v1/system/plans` — plans are rooted at the `/v1/system` namespace. */
  private readonly baseUrl = `${environment.apiBaseUrl}/system/plans`;
  /** `/api/v1/system/tenants` — per-tenant override sub-resource. */
  private readonly tenantsUrl = `${environment.apiBaseUrl}/system/tenants`;

  /** AC-1 / FR-5: list all plans (with active tenant count). */
  list(): Observable<IPlanSummary[]> {
    return this.http.get<IPlanSummary[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** AC-2: full plan for the editor. */
  get(id: string): Observable<IPlanDetail> {
    return this.http.get<IPlanDetail>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  /** AC-2: create a new plan. May 409 on a duplicate `code` (FR-3). */
  create(payload: IPlanUpsert): Observable<IPlanDetail> {
    return this.http.post<IPlanDetail>(this.baseUrl, payload, {
      withCredentials: true,
    });
  }

  /** AC-3: update an existing plan (`code` is ignored server-side, FR-3). */
  update(id: string, payload: IPlanUpsert): Observable<IPlanDetail> {
    return this.http.put<IPlanDetail>(`${this.baseUrl}/${id}`, payload, {
      withCredentials: true,
    });
  }

  /** AC-4: archive a plan (sets is_active = false). */
  archive(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/archive`, null, {
      withCredentials: true,
    });
  }

  /**
   * FR-7: delete a plan. The BE returns 409 if any tenant references it — the
   * caller surfaces a clear "can only be archived" message in that case.
   */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  // ─── Per-tenant plan limit overrides (AC-5 / FR-4) ───────────

  /** GET overrides currently configured for a tenant. */
  getOverrides(tenantId: string): Observable<IPlanLimitOverride[]> {
    return this.http.get<IPlanLimitOverride[]>(
      `${this.tenantsUrl}/${tenantId}/plan-overrides`,
      { withCredentials: true },
    );
  }

  /** PUT the full set of overrides for a tenant (replace semantics). */
  saveOverrides(
    tenantId: string,
    overrides: IPlanLimitOverride[],
  ): Observable<IPlanLimitOverride[]> {
    return this.http.put<IPlanLimitOverride[]>(
      `${this.tenantsUrl}/${tenantId}/plan-overrides`,
      overrides,
      { withCredentials: true },
    );
  }

  /** DELETE a single override (by limit key) for a tenant. */
  deleteOverride(tenantId: string, limitKey: string): Observable<void> {
    return this.http.delete<void>(
      `${this.tenantsUrl}/${tenantId}/plan-overrides/${limitKey}`,
      { withCredentials: true },
    );
  }
}
