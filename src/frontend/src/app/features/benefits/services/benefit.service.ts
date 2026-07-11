import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IBenefitPlan,
  ICreateBenefitPlan,
  IUpdateBenefitPlan,
  IChangeBenefitPlanStatus,
  IEligibilityRule,
  ICreateEligibilityRule,
  IEligiblePlan,
  IEnrollRequest,
  IBenefitEnrollment,
  ITerminateEnrollmentRequest,
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

  // =============================================================
  // US-TRN-003: Eligibility rules (Manage) + enrollment
  //
  // Backend contract (`api/v1/tenant/benefits`):
  //   GET    /plans/:planId/eligibility-rules      - list rules (View.All/Manage)
  //   POST   /plans/:planId/eligibility-rules      - add rule (Manage)
  //   DELETE /eligibility-rules/:id                - remove rule (Manage)
  //   GET    /eligible                             - plans I qualify for (View.Own+)
  //   POST   /enrollments                          - enroll self/other
  //   POST   /enrollments/:id/terminate            - terminate an enrollment
  //   GET    /me/enrollments                       - my enrollments (View.Own+)
  //   GET    /employees/:employeeId/enrollments    - an employee's enrollments
  // =============================================================

  /** List the eligibility rules for a plan (View.All/Manage). */
  getEligibilityRules(planId: string): Observable<IEligibilityRule[]> {
    return this.http.get<IEligibilityRule[]>(
      `${this.baseUrl}/plans/${planId}/eligibility-rules`,
      { withCredentials: true }
    );
  }

  /** Add an eligibility rule to a plan (Manage). */
  createEligibilityRule(
    planId: string,
    request: ICreateEligibilityRule
  ): Observable<IEligibilityRule> {
    return this.http.post<IEligibilityRule>(
      `${this.baseUrl}/plans/${planId}/eligibility-rules`,
      request,
      { withCredentials: true }
    );
  }

  /** Remove an eligibility rule (Manage). */
  deleteEligibilityRule(ruleId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/eligibility-rules/${ruleId}`,
      { withCredentials: true }
    );
  }

  /** Plans the current user's employee qualifies for right now (self-service). */
  getEligiblePlans(): Observable<IEligiblePlan[]> {
    return this.http.get<IEligiblePlan[]>(`${this.baseUrl}/eligible`, {
      withCredentials: true,
    });
  }

  /**
   * Enroll in a plan. Null employeeId → the current user's employee (View.Own);
   * a non-null employeeId enrolls another employee (Manage).
   */
  enroll(request: IEnrollRequest): Observable<IBenefitEnrollment> {
    return this.http.post<IBenefitEnrollment>(
      `${this.baseUrl}/enrollments`,
      request,
      { withCredentials: true }
    );
  }

  /** Terminate an enrollment (Terminated + EndDate; default today). */
  terminate(
    enrollmentId: string,
    request: ITerminateEnrollmentRequest = {}
  ): Observable<IBenefitEnrollment> {
    return this.http.post<IBenefitEnrollment>(
      `${this.baseUrl}/enrollments/${enrollmentId}/terminate`,
      request,
      { withCredentials: true }
    );
  }

  /** The current user's employee's enrollments (self-service). */
  getMyEnrollments(): Observable<IBenefitEnrollment[]> {
    return this.http.get<IBenefitEnrollment[]>(
      `${this.baseUrl}/me/enrollments`,
      { withCredentials: true }
    );
  }

  /** An employee's enrollments (self via View.Own, others via View.All/Manage). */
  getEmployeeEnrollments(
    employeeId: string
  ): Observable<IBenefitEnrollment[]> {
    return this.http.get<IBenefitEnrollment[]>(
      `${this.baseUrl}/employees/${employeeId}/enrollments`,
      { withCredentials: true }
    );
  }
}
