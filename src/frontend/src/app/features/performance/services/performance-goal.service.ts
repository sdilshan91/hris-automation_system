import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ActiveCycleWire,
  IAppraisalCycle,
  IGoal,
  ISaveGoalsRequest,
  ITeamGoalStatus,
  mapActiveCycle,
} from '../models/goal.models';

/**
 * US-PRF-001: Service for tenant-scoped performance goal-setting.
 *
 * Thin and isolated by design — the route strings live here ONLY, so a backend
 * contract change is a one-file fix. All requests use withCredentials (httpOnly
 * cookie auth) and are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header). The backend stamps tenant_id + audit fields and enforces RLS + the
 * Performance.SetGoal.Team permission (BR-4, NFR-2).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see goal.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class PerformanceGoalService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance`;

  /**
   * The current tenant's active appraisal cycle plus the authoritative
   * goal-setting window state (drives AC-1 / AC-5). The backend computes
   * `goalSettingOpen` from the configured window dates (BR-1).
   */
  getActiveCycle(): Observable<IAppraisalCycle> {
    return this.http
      .get<ActiveCycleWire>(`${this.baseUrl}/cycles/active`, {
        withCredentials: true,
      })
      .pipe(map(mapActiveCycle));
  }

  /**
   * The manager's direct reports with their goal-setting status + progress for a
   * cycle (AC-4). Tolerates either a bare array or a `{ data }`-style page so a
   * backend pagination choice doesn't break the dashboard.
   */
  getTeamStatus(cycleId: string): Observable<ITeamGoalStatus[]> {
    return this.http
      .get<ITeamGoalStatus[] | { data: ITeamGoalStatus[] }>(
        `${this.baseUrl}/cycles/${cycleId}/team-dashboard`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /** Goals already saved for one employee in a cycle (AC-1 prefill). */
  getEmployeeGoals(
    cycleId: string,
    employeeId: string,
  ): Observable<IGoal[]> {
    return this.http
      .get<IGoal[] | { goals?: IGoal[]; data?: IGoal[] }>(
        `${this.baseUrl}/employees/${employeeId}/cycles/${cycleId}/goals`,
        { withCredentials: true },
      )
      // Same endpoint returns EmployeeGoalsDto ({ goals, totalWeight, … }); read its
      // `goals`, tolerating a bare array / legacy `{ data }` shape too (BUG-243).
      .pipe(
        map((res) =>
          Array.isArray(res) ? res : (res?.goals ?? res?.data ?? []),
        ),
      );
  }

  /**
   * Replace the full goal set for an employee in a cycle (AC-2). The backend
   * re-validates the 100% weight sum + 1-10 count server-side and notifies the
   * employee (FR-7). Returns the persisted goals.
   */
  saveGoals(
    cycleId: string,
    employeeId: string,
    request: ISaveGoalsRequest,
  ): Observable<IGoal[]> {
    return this.http
      .put<IGoal[] | { goals?: IGoal[]; data?: IGoal[] }>(
        `${this.baseUrl}/employees/${employeeId}/cycles/${cycleId}/goals`,
        request,
        { withCredentials: true },
      )
      // The bulk endpoint returns EmployeeGoalsDto ({ goals, totalWeight, … }); read
      // its `goals`, tolerating a bare array / legacy `{ data }` shape too.
      .pipe(
        map((res) =>
          Array.isArray(res) ? res : (res?.goals ?? res?.data ?? []),
        ),
      );
  }

  /**
   * BUG-056: finalize (lock) an employee's goal set for a cycle. The backend
   * re-validates that the goals sum to exactly 100% (422 `weight_not_100`), rejects a
   * set that is already finalized (409 `goals_finalized`), and enforces authz
   * (403/404). On success every goal comes back with status === 'Finalized'.
   *
   * URL: POST /api/v1/tenant/performance/goals/finalize — the tenant-scoped route
   * confirmed by the backend, matching every sibling goal endpoint. Built from the
   * shared `baseUrl` (`${apiBaseUrl}/tenant/performance`) + `/goals/finalize`.
   */
  finalizeGoals(employeeId: string, cycleId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/goals/finalize`,
      { employeeId, cycleId },
      { withCredentials: true },
    );
  }

  /**
   * DF-46: re-open (unlock) a finalized goal set so it can be edited again. The
   * backend requires a MANDATORY `reason` (recorded for audit), enforces authz
   * (403 — HR or the direct manager only), and rejects a set that is not finalized
   * (409 `goals_not_finalized`). On success (204/void) the set returns to its
   * editable state; the caller reloads the goals to reflect it.
   *
   * URL: POST /api/v1/tenant/performance/goals/reopen — the tenant-scoped route
   * confirmed by the backend, mirroring the sibling `goals/finalize` endpoint. Built
   * from the shared `baseUrl` (`${apiBaseUrl}/tenant/performance`) + `/goals/reopen`.
   */
  reopenGoals(
    employeeId: string,
    cycleId: string,
    reason: string,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/goals/reopen`,
      { employeeId, cycleId, reason },
      { withCredentials: true },
    );
  }

  /** Accept either a bare array or a `{ data }` page; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
