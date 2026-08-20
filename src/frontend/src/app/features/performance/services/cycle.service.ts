import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  CycleDashboardWire,
  CycleSummaryWire,
  CycleWire,
  ICloneCycleRequest,
  ICycle,
  ICycleDashboard,
  ICycleSummary,
  ICycleTransitionRequest,
  ISaveCycleRequest,
  mapCycle,
  mapCycleDashboard,
  mapCycleSummary,
} from '../models/cycle.models';

/**
 * US-PRF-004: Service for HR appraisal-cycle management (cycle CRUD + dashboard +
 * status transitions + clone). Sibling to PerformanceGoalService (US-PRF-001),
 * SelfAssessmentService (US-PRF-002), and ManagerReviewService (US-PRF-003).
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the tenant
 * from the session and enforces `Performance.SetGoal.All` / `Performance.Publish.All`
 * + RLS (BR-1 / NFR-2), so the FE sends no tenant id.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see cycle.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class CycleService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance/cycles`;

  /** All cycles for the tenant (FR-7). Tolerates a bare array or a `{ data }` page. */
  list(): Observable<ICycleSummary[]> {
    return this.http
      .get<CycleSummaryWire[] | { data: CycleSummaryWire[] }>(this.baseUrl, {
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res).map(mapCycleSummary)));
  }

  /** Full cycle detail incl. phases + participant scope (AC-5 edit prefill). */
  get(cycleId: string): Observable<ICycle> {
    return this.http
      .get<CycleWire>(`${this.baseUrl}/${cycleId}`, { withCredentials: true })
      .pipe(map(mapCycle));
  }

  /**
   * Create a cycle (AC-1/AC-2). The backend re-validates phase sequencing (FR-2),
   * scopes participants (FR-3), schedules Hangfire reminder jobs (AC-2/FR-5), and
   * returns the persisted cycle.
   */
  create(request: ISaveCycleRequest): Observable<ICycle> {
    return this.http
      .post<CycleWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapCycle));
  }

  /**
   * Edit a cycle / extend a phase deadline (AC-5). The backend re-validates
   * sequencing, reschedules affected Hangfire jobs, and notifies participants.
   */
  update(cycleId: string, request: ISaveCycleRequest): Observable<ICycle> {
    return this.http
      .put<CycleWire>(`${this.baseUrl}/${cycleId}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapCycle));
  }

  /** Phase timeline + completion/overdue stats for the dashboard (AC-3). */
  dashboard(cycleId: string): Observable<ICycleDashboard> {
    return this.http
      .get<CycleDashboardWire>(`${this.baseUrl}/${cycleId}/dashboard`, {
        withCredentials: true,
      })
      .pipe(map(mapCycleDashboard));
  }

  /**
   * Status transition (FR-7): Activate / Pause / Resume / Complete / Cancel. The
   * backend enforces the legal source-status (BR-5 locks the scale on Activate) and
   * requires a reason on Cancel (BR-6). Returns the updated cycle.
   */
  transition(
    cycleId: string,
    request: ICycleTransitionRequest,
  ): Observable<ICycle> {
    // BUG-243: the backend route is POST cycles/{id}/status (not /transition).
    return this.http
      .post<CycleWire>(`${this.baseUrl}/${cycleId}/status`, request, {
        withCredentials: true,
      })
      .pipe(map(mapCycle));
  }

  /**
   * Clone a cycle as a template for the next period (FR-8). BUG-243: the backend
   * route is POST cycles/clone with the source id carried in the body as
   * `sourceCycleId` (CloneCycleInput) — not a path segment.
   */
  clone(cycleId: string, request: ICloneCycleRequest): Observable<ICycle> {
    return this.http
      .post<CycleWire>(
        `${this.baseUrl}/clone`,
        { sourceCycleId: cycleId, ...request },
        { withCredentials: true },
      )
      .pipe(map(mapCycle));
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
