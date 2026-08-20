import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, switchMap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IManagerReview,
  IManagerTeamRow,
  ISaveManagerReviewRequest,
  ManagerReviewWire,
  TeamReviewsDashboardWire,
  mapManagerReview,
  mapTeamReviewsDashboard,
} from '../models/manager-review.models';

/**
 * US-PRF-003: Service for the MANAGER-side performance review experience (manager
 * rates direct reports). Sibling to PerformanceGoalService (goal-setting, US-PRF-001)
 * and SelfAssessmentService (employee self-rating, US-PRF-002).
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the manager
 * + tenant from the session and enforces `Performance.Review.Team` (BR-2: only direct
 * reports) / `Performance.Review.All` (BR-3: HR) + RLS (NFR-2), so the FE sends no
 * tenant id.
 *
 * BUG-243: reconciled to the real ManagerReviewController routes under
 * `.../tenant/performance/reviews`, which are keyed by an explicit cycleId. The
 * team/per-employee reads resolve the active cycleId INSIDE this service via
 * `GET .../cycles/active` (CyclesController.GetActive) + `switchMap`, so the public
 * method signatures stay stable; draft/submit carry cycleId + employeeId in the body.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see manager-review.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class ManagerReviewService {
  private readonly http = inject(HttpClient);
  private readonly perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  private readonly reviewsBase = `${this.perfBase}/reviews`;

  /** Resolve the active cycle's id (CyclesController.GetActive admits Review.Team). */
  private activeCycleId(): Observable<string> {
    return this.http
      .get<{ id: string }>(`${this.perfBase}/cycles/active`, {
        withCredentials: true,
      })
      .pipe(map((c) => c.id));
  }

  /**
   * The manager's direct reports for the active cycle with their review status
   * (AC-4). Resolves the active cycleId first. Tolerates either a bare array or a
   * `{ data }`-style page so a backend pagination choice doesn't break the dashboard.
   */
  getTeam(): Observable<IManagerTeamRow[]> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get<TeamReviewsDashboardWire>(
          `${this.reviewsBase}/cycles/${cycleId}/team`,
          { withCredentials: true },
        ),
      ),
      map(mapTeamReviewsDashboard),
    );
  }

  /**
   * Load one employee's manager review for the active cycle (AC-1): each goal with
   * the employee's self-rating/comment + the manager's rating/comment, the rating
   * scale, the window/lock state, and any computed scores. Resolves the active
   * cycleId, then loads the per-employee workspace (NFR-1 ≤400ms).
   */
  getEmployeeReview(employeeId: string): Observable<IManagerReview> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get<ManagerReviewWire>(
          `${this.reviewsBase}/cycles/${cycleId}/employees/${employeeId}`,
          { withCredentials: true },
        ),
      ),
      map(mapManagerReview),
    );
  }

  /**
   * Persist a partial draft (NFR-3). No all-goals-rated gate server-side; status is
   * unchanged. cycleId + employeeId travel in the body. Returns the persisted review.
   */
  saveDraft(request: ISaveManagerReviewRequest): Observable<IManagerReview> {
    return this.http
      .put<ManagerReviewWire>(`${this.reviewsBase}/draft`, request, {
        withCredentials: true,
      })
      .pipe(map(mapManagerReview));
  }

  /**
   * Submit the manager review (AC-2). The backend re-validates all-goals-rated +
   * each manager comment ≥20 chars (AC-3), computes the weighted manager score +
   * final combined score (FR-4 / BR-4), flips the status to `ManagerReviewSubmitted`,
   * locks edits (AC-5), and notifies the employee. Rejects when the window is closed
   * (BR-1). cycleId + employeeId travel in the body. Returns the locked review.
   */
  submit(request: ISaveManagerReviewRequest): Observable<IManagerReview> {
    return this.http
      .post<ManagerReviewWire>(`${this.reviewsBase}/submit`, request, {
        withCredentials: true,
      })
      .pipe(map(mapManagerReview));
  }
}
