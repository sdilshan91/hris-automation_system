import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IPlanSummary,
  IPlanDetail,
  IPlanUpsert,
  IPlanLimitOverride,
  IPlanLimitOverrideDraft,
  IPlanCreateResult,
  PlanListItemWire,
  PlanDetailWire,
  PlanCreateResultWire,
  PlanLimitOverrideWire,
  PlanLimitOverrideUpsertWire,
  mapPlanSummary,
  mapPlanDetail,
  mapPlanCreateResult,
  mapPlanLimitOverride,
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
 *   GET    /system/plans/overrides?tenantId={id}          list a tenant's overrides (AC-5)
 *   POST   /system/plans/overrides                        upsert ONE override (AC-5)
 *   DELETE /system/plans/overrides/{overrideId}           remove ONE override, keyed by id (AC-5)
 *
 * BUG-471: the override routes were previously addressed as a per-tenant sub-resource
 * (`/system/tenants/{tenantId}/plan-overrides`) that the API has never served — all three
 * were live 404s, so the admin console could read but never write an override. They are
 * now plan-rooted, matching AdminPlansController. The overrides API is single-item
 * (upsert-one / delete-one-by-id); the UI drives it one action at a time.
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
  /** `/api/v1/system/plans/overrides` — plan-rooted, tenant selected by query/body. */
  private readonly overridesUrl = `${environment.apiBaseUrl}/system/plans/overrides`;

  /** AC-1 / FR-5: list all plans (with active tenant count). */
  list(): Observable<IPlanSummary[]> {
    return this.http
      .get<PlanListItemWire[]>(this.baseUrl, { withCredentials: true })
      .pipe(map((rows) => (rows ?? []).map(mapPlanSummary)));
  }

  /** AC-2: full plan for the editor. */
  get(id: string): Observable<IPlanDetail> {
    return this.http
      .get<PlanDetailWire>(`${this.baseUrl}/${id}`, { withCredentials: true })
      .pipe(map(mapPlanDetail));
  }

  /**
   * AC-2: create a new plan. May 409 on a duplicate `code` (FR-3).
   *
   * Returns `IPlanCreateResult`, not `IPlanDetail`: the endpoint's response DTO is
   * `SubscriptionPlansCreatePlanResultDto` = `{ id, code }`. The old `IPlanDetail`
   * return type was an unchecked cast asserting 20 fields that are never sent.
   */
  create(payload: IPlanUpsert): Observable<IPlanCreateResult> {
    return this.http
      .post<PlanCreateResultWire>(this.baseUrl, payload, {
        withCredentials: true,
      })
      .pipe(map(mapPlanCreateResult));
  }

  /**
   * AC-3: update an existing plan (`code` is ignored server-side, FR-3).
   *
   * Returns `void`: the endpoint responds with a bare `ApiResponse` carrying no
   * `data`, so after the envelope interceptor there is nothing to map. The old
   * `IPlanDetail` return type described an object that was always `undefined`.
   */
  update(id: string, payload: IPlanUpsert): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, payload, {
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

  /** GET the overrides currently configured for a tenant (AC-5). */
  getOverrides(tenantId: string): Observable<IPlanLimitOverride[]> {
    return this.http
      .get<PlanLimitOverrideWire[]>(this.overridesUrl, {
        params: { tenantId },
        withCredentials: true,
      })
      .pipe(map((rows) => (rows ?? []).map(mapPlanLimitOverride)));
  }

  /**
   * Create or update ONE override and return the persisted row (AC-5 / FR-4).
   *
   * The endpoint is an upsert keyed on (tenantId, limitKey), so re-posting an existing
   * key replaces its value rather than duplicating it. `limitKey` must be a canonical
   * snake_case key (OVERRIDE_LIMIT_FIELDS) or the BE rejects it as `limit_key_invalid`.
   */
  upsertOverride(
    tenantId: string,
    override: IPlanLimitOverrideDraft,
  ): Observable<IPlanLimitOverride> {
    const body: PlanLimitOverrideUpsertWire = {
      tenantId,
      limitKey: override.limitKey,
      value: override.value,
      expiresAt: override.expiresAt,
    };
    return this.http
      .post<PlanLimitOverrideWire>(this.overridesUrl, body, {
        withCredentials: true,
      })
      .pipe(map(mapPlanLimitOverride));
  }

  /** DELETE one override by its own id (NOT its limit key) (AC-5). */
  deleteOverride(overrideId: string): Observable<void> {
    return this.http.delete<void>(`${this.overridesUrl}/${overrideId}`, {
      withCredentials: true,
    });
  }
}
