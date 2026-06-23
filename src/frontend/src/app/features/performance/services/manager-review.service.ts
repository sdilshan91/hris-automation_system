import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IManagerReview,
  IManagerTeamRow,
  ISaveManagerReviewRequest,
} from '../models/manager-review.models';

/**
 * US-PRF-003: Service for the MANAGER-side performance review experience (manager
 * rates direct reports). Sibling to PerformanceGoalService (goal-setting, US-PRF-001)
 * and SelfAssessmentService (employee self-rating, US-PRF-002).
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix
 * (reconcile at US-PRF-004). All requests use withCredentials (httpOnly cookie auth)
 * and are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The
 * backend resolves the manager + tenant from the session and enforces
 * `Performance.Review.Team` (BR-2: only direct reports) / `Performance.Review.All`
 * (BR-3: HR) + RLS (NFR-2), so the FE sends no tenant id.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see manager-review.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class ManagerReviewService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance/manager-review`;

  /**
   * The manager's direct reports for the active cycle with their review status
   * (AC-4). Tolerates either a bare array or a `{ data }`-style page so a backend
   * pagination choice doesn't break the dashboard.
   */
  getTeam(): Observable<IManagerTeamRow[]> {
    return this.http
      .get<IManagerTeamRow[] | { data: IManagerTeamRow[] }>(
        `${this.baseUrl}/cycles/active/team`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /**
   * Load one employee's manager review for the active cycle (AC-1): each goal with
   * the employee's self-rating/comment + the manager's rating/comment, the rating
   * scale, the window/lock state, and any computed scores. One call drives the whole
   * per-employee review screen (NFR-1 ≤400ms).
   */
  getEmployeeReview(employeeId: string): Observable<IManagerReview> {
    return this.http.get<IManagerReview>(
      `${this.baseUrl}/employees/${employeeId}/active`,
      { withCredentials: true },
    );
  }

  /**
   * Persist a partial draft (NFR-3). No all-goals-rated gate server-side; status is
   * unchanged. Returns the persisted review.
   */
  saveDraft(
    reviewId: string,
    request: ISaveManagerReviewRequest,
  ): Observable<IManagerReview> {
    return this.http.put<IManagerReview>(
      `${this.baseUrl}/${reviewId}/draft`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Submit the manager review (AC-2). The backend re-validates all-goals-rated +
   * each manager comment ≥20 chars (AC-3), computes the weighted manager score +
   * final combined score (FR-4 / BR-4), flips the status to `ManagerReviewSubmitted`,
   * locks edits (AC-5), and notifies the employee. Rejects when the window is closed
   * (BR-1). Returns the locked review.
   */
  submit(
    reviewId: string,
    request: ISaveManagerReviewRequest,
  ): Observable<IManagerReview> {
    return this.http.post<IManagerReview>(
      `${this.baseUrl}/${reviewId}/submit`,
      request,
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
