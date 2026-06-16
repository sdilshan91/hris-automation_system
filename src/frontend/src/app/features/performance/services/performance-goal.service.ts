import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAppraisalCycle,
  IGoal,
  ISaveGoalsRequest,
  ITeamGoalStatus,
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
  private readonly baseUrl = `${environment.apiBaseUrl}/performance`;

  /**
   * The current tenant's active appraisal cycle plus the authoritative
   * goal-setting window state (drives AC-1 / AC-5). The backend computes
   * `goalSettingOpen` from the configured window dates (BR-1).
   */
  getActiveCycle(): Observable<IAppraisalCycle> {
    return this.http.get<IAppraisalCycle>(`${this.baseUrl}/cycles/active`, {
      withCredentials: true,
    });
  }

  /**
   * The manager's direct reports with their goal-setting status + progress for a
   * cycle (AC-4). Tolerates either a bare array or a `{ data }`-style page so a
   * backend pagination choice doesn't break the dashboard.
   */
  getTeamStatus(cycleId: string): Observable<ITeamGoalStatus[]> {
    return this.http
      .get<ITeamGoalStatus[] | { data: ITeamGoalStatus[] }>(
        `${this.baseUrl}/cycles/${cycleId}/team`,
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
      .get<IGoal[] | { data: IGoal[] }>(
        `${this.baseUrl}/cycles/${cycleId}/employees/${employeeId}/goals`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
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
      .put<IGoal[] | { data: IGoal[] }>(
        `${this.baseUrl}/cycles/${cycleId}/employees/${employeeId}/goals`,
        request,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
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
