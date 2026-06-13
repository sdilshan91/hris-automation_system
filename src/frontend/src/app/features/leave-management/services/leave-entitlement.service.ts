import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IEntitlementRule,
  ICreateEntitlementRuleRequest,
  IUpdateEntitlementRuleRequest,
  IInlineUpdateRequest,
  IEntitlementOverride,
  IUpsertOverrideRequest,
  IEffectiveEntitlement,
  IBulkEntitlementRequest,
  IBulkEntitlementResponse,
  IEntitlementRuleFilter,
} from '../models/leave-entitlement.models';

/**
 * US-LV-002: Service for leave entitlement CRUD + overrides + bulk operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET    /api/v1/tenant/leave-entitlements/rules              - list all rules
 *   GET    /api/v1/tenant/leave-entitlements/rules/:id          - single rule
 *   POST   /api/v1/tenant/leave-entitlements/rules              - create rule
 *   PUT    /api/v1/tenant/leave-entitlements/rules/:id          - update rule
 *   PATCH  /api/v1/tenant/leave-entitlements/rules/:id/days     - inline update days only
 *   DELETE /api/v1/tenant/leave-entitlements/rules/:id          - delete rule
 *
 *   GET    /api/v1/tenant/leave-entitlements/overrides?employeeId=...  - list overrides
 *   POST   /api/v1/tenant/leave-entitlements/overrides                 - upsert override
 *   DELETE /api/v1/tenant/leave-entitlements/overrides/:id             - delete override
 *
 *   GET    /api/v1/tenant/leave-entitlements/compute-effective?employeeId=...  - computed
 *   POST   /api/v1/tenant/leave-entitlements/bulk                              - bulk assign
 */
@Injectable({ providedIn: 'root' })
export class LeaveEntitlementService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements`;

  // ─── Rules CRUD ───────────────────────────────────────────

  /** List all entitlement rules, optionally filtered */
  getRules(filter?: IEntitlementRuleFilter): Observable<IEntitlementRule[]> {
    let params = new HttpParams();
    if (filter?.leaveTypeId) {
      params = params.set('leaveTypeId', filter.leaveTypeId);
    }
    if (filter?.departmentId) {
      params = params.set('departmentId', filter.departmentId);
    }
    if (filter?.employmentType) {
      params = params.set('employmentType', filter.employmentType);
    }
    if (filter?.activeOnly !== undefined) {
      params = params.set('activeOnly', String(filter.activeOnly));
    }
    return this.http.get<IEntitlementRule[]>(`${this.baseUrl}/rules`, {
      params,
      withCredentials: true,
    });
  }

  /** Get a single rule by ID */
  getRule(ruleId: string): Observable<IEntitlementRule> {
    return this.http.get<IEntitlementRule>(`${this.baseUrl}/rules/${ruleId}`, {
      withCredentials: true,
    });
  }

  /** Create a new entitlement rule (FR-1) */
  createRule(request: ICreateEntitlementRuleRequest): Observable<IEntitlementRule> {
    return this.http.post<IEntitlementRule>(`${this.baseUrl}/rules`, request, {
      withCredentials: true,
    });
  }

  /** Update an existing entitlement rule */
  updateRule(ruleId: string, request: IUpdateEntitlementRuleRequest): Observable<IEntitlementRule> {
    return this.http.put<IEntitlementRule>(`${this.baseUrl}/rules/${ruleId}`, request, {
      withCredentials: true,
    });
  }

  /** Inline update only the entitlement days for a rule (matrix cell edit) */
  updateRuleDays(ruleId: string, request: IInlineUpdateRequest): Observable<IEntitlementRule> {
    return this.http.patch<IEntitlementRule>(`${this.baseUrl}/rules/${ruleId}/days`, request, {
      withCredentials: true,
    });
  }

  /** Delete an entitlement rule */
  deleteRule(ruleId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/rules/${ruleId}`, {
      withCredentials: true,
    });
  }

  // ─── Overrides (AC-3) ─────────────────────────────────────

  /** List overrides for a specific employee */
  getOverrides(employeeId: string, leaveYear?: number): Observable<IEntitlementOverride[]> {
    let params = new HttpParams().set('employeeId', employeeId);
    if (leaveYear !== undefined) {
      params = params.set('leaveYear', String(leaveYear));
    }
    return this.http.get<IEntitlementOverride[]>(`${this.baseUrl}/overrides`, {
      params,
      withCredentials: true,
    });
  }

  /** Create or update (upsert) a per-employee override */
  upsertOverride(employeeId: string, request: IUpsertOverrideRequest): Observable<IEntitlementOverride> {
    return this.http.post<IEntitlementOverride>(
      `${this.baseUrl}/overrides`,
      { ...request, employeeId },
      { withCredentials: true },
    );
  }

  /** Delete an override */
  deleteOverride(overrideId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/overrides/${overrideId}`, {
      withCredentials: true,
    });
  }

  // ─── Computed effective ───────────────────────────────────

  /** Get computed effective entitlements for an employee */
  getEffectiveEntitlements(employeeId: string): Observable<IEffectiveEntitlement[]> {
    const params = new HttpParams().set('employeeId', employeeId);
    return this.http.get<IEffectiveEntitlement[]>(`${this.baseUrl}/compute-effective`, {
      params,
      withCredentials: true,
    });
  }

  // ─── Bulk (FR-4) ──────────────────────────────────────────

  /** Bulk assign entitlements to multiple employees */
  bulkAssign(request: IBulkEntitlementRequest): Observable<IBulkEntitlementResponse> {
    return this.http.post<IBulkEntitlementResponse>(`${this.baseUrl}/bulk`, request, {
      withCredentials: true,
    });
  }

  // ─── Error helper ─────────────────────────────────────────

  /** Parse an error response into a message string */
  static parseError(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred.';
  }
}
