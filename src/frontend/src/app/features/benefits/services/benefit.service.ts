import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IBenefitPlan,
  ICreateBenefitPlan,
  IUpdateBenefitPlan,
  IChangeBenefitPlanStatus,
} from '../models/benefit.models';

/**
 * US-TRN-002: Service for benefit-plan administration.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend contract (`api/v1/tenant/benefits`):
 *   GET  /plans          - list plans (View.Own/View.All/Manage)
 *   GET  /plans/:id      - single plan
 *   POST /plans          - create Draft plan (Manage)
 *   PUT  /plans/:id      - update plan metadata/cost/coverage/dates (Manage)
 *   POST /plans/:id/status - change status: activate/deactivate/archive (Manage)
 */
@Injectable({ providedIn: 'root' })
export class BenefitService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/benefits`;

  // --- Plans (read) ------------------------------------------

  /** List benefit plans for the current tenant (AC-5; tenant-isolated server-side). */
  getPlans(): Observable<IBenefitPlan[]> {
    return this.http.get<IBenefitPlan[]>(`${this.baseUrl}/plans`, {
      withCredentials: true,
    });
  }

  /** Get a single plan by ID. */
  getPlan(planId: string): Observable<IBenefitPlan> {
    return this.http.get<IBenefitPlan>(`${this.baseUrl}/plans/${planId}`, {
      withCredentials: true,
    });
  }

  // --- Plans (write, Manage) ---------------------------------

  /** Create a new Draft plan (AC-1). Null currency defaults to the tenant currency. */
  createPlan(request: ICreateBenefitPlan): Observable<IBenefitPlan> {
    return this.http.post<IBenefitPlan>(`${this.baseUrl}/plans`, request, {
      withCredentials: true,
    });
  }

  /** Update an existing plan's metadata/cost/coverage/dates/window (AC-4). */
  updatePlan(
    planId: string,
    request: IUpdateBenefitPlan
  ): Observable<IBenefitPlan> {
    return this.http.put<IBenefitPlan>(
      `${this.baseUrl}/plans/${planId}`,
      request,
      { withCredentials: true }
    );
  }

  /** Change a plan's status: Draft→Active, Active→Inactive, *→Archived, … (AC-2/AC-3/AC-6). */
  changePlanStatus(
    planId: string,
    request: IChangeBenefitPlanStatus
  ): Observable<IBenefitPlan> {
    return this.http.post<IBenefitPlan>(
      `${this.baseUrl}/plans/${planId}/status`,
      request,
      { withCredentials: true }
    );
  }
}
