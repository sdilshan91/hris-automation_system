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
  IPlanCreateResult,
  PlanListItemWire,
  PlanDetailWire,
  PlanCreateResultWire,
  PlanLimitOverrideWire,
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
 *   GET    /system/tenants/{tenantId}/plan-overrides      list overrides (AC-5)      ← 404, see below
 *   PUT    /system/tenants/{tenantId}/plan-overrides      replace overrides (AC-5)   ← 404, see below
 *   DELETE /system/tenants/{tenantId}/plan-overrides/{key} remove one override       ← 404, see below
 *
 * ⚠ REPORTED, NOT FIXED HERE (D1 admin wire-type migration is types-only): the three
 * per-tenant override routes above DO NOT EXIST in contracts/openapi/hrm-v1.json. The
 * API serves overrides at a PLAN-rooted, non-tenant-scoped address with different verbs
 * and cardinality:
 *     GET    /system/plans/overrides                 → PlanLimitOverrideDto[]
 *     POST   /system/plans/overrides                 → upserts ONE override
 *     DELETE /system/plans/overrides/{overrideId}    → keyed by override ID, not limitKey
 * Repointing them is a behaviour change (`saveOverrides` replace-set vs single upsert;
 * `deleteOverride` limitKey vs id) that needs a product decision, so this pass migrates
 * only the TYPES and leaves the URLs alone rather than guessing.
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

  /**
   * GET overrides currently configured for a tenant.
   *
   * ⚠ The URL is a live 404 (see the class docstring) — the API serves this at
   * `/system/plans/overrides`. The response TYPE is migrated to the contract DTO the
   * real route returns, which is the shape this method has always meant to consume.
   */
  getOverrides(tenantId: string): Observable<IPlanLimitOverride[]> {
    return this.http
      .get<PlanLimitOverrideWire[]>(
        `${this.tenantsUrl}/${tenantId}/plan-overrides`,
        { withCredentials: true },
      )
      .pipe(map((rows) => (rows ?? []).map(mapPlanLimitOverride)));
  }

  /**
   * PUT the full set of overrides for a tenant (replace semantics).
   *
   * ⚠ Live 404 (see the class docstring). The real API upserts ONE override per POST
   * to `/system/plans/overrides` and returns a single DTO, so replace-set semantics do
   * not exist server-side at all — reconciling that needs a decision, not a mapper.
   */
  saveOverrides(
    tenantId: string,
    overrides: IPlanLimitOverride[],
  ): Observable<IPlanLimitOverride[]> {
    return this.http
      .put<PlanLimitOverrideWire[]>(
        `${this.tenantsUrl}/${tenantId}/plan-overrides`,
        overrides,
        { withCredentials: true },
      )
      .pipe(map((rows) => (rows ?? []).map(mapPlanLimitOverride)));
  }

  /**
   * DELETE a single override (by limit key) for a tenant.
   *
   * ⚠ Live 404 (see the class docstring), and additionally unreachable: no component
   * calls this method. The real route keys on the override's `id`, not its `limitKey`.
   */
  deleteOverride(tenantId: string, limitKey: string): Observable<void> {
    return this.http.delete<void>(
      `${this.tenantsUrl}/${tenantId}/plan-overrides/${limitKey}`,
      { withCredentials: true },
    );
  }
}
