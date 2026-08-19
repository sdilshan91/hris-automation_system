import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAutoGeneratePreview,
  IAutoGenerateRequest,
  IBudgetTracker,
  ICompletedCycleOption,
  IRecommendationRow,
  IRecommendationSummary,
  IRecommendationWorkspace,
  IUpdateRecommendationRequest,
  RecommendationExportFormat,
  RecommendationWorkspaceWire,
  mapRecommendationWorkspace,
} from '../models/recommendation.models';

/**
 * US-PRF-010: service for performance-based recommendations (promotion, bonus,
 * increment, …). Covers BOTH personas: the HR org-wide workspace
 * (`Performance.Publish.All`) and a manager's "Team Recommendations"
 * (`Performance.Read.Team`, direct reports only — AC-5). The backend authorizes each
 * by session role + ownership; the FE sends no tenant/employee id.
 *
 * Sibling to the other Performance services (US-PRF-001..009). The route strings + the
 * filter→query-param mapping live HERE ONLY, so a backend contract change is a
 * one-file fix. All requests use withCredentials (httpOnly cookie auth) and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header) + RLS (NFR-2).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips `{ data }`,
 * so the JSON methods consume BARE payloads. Enums arrive as PascalCase STRINGS
 * (US-PLT-003). Exports stream as a Blob (Content-Disposition filename), NOT unwrapped.
 */
@Injectable({ providedIn: 'root' })
export class RecommendationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance/recommendations`;

  /** BR-1: completed cycles (published final ratings) — the cycle picker. */
  getCompletedCycles(): Observable<ICompletedCycleOption[]> {
    return this.http
      .get<
        ICompletedCycleOption[] | { data: ICompletedCycleOption[] }
      >(`${this.baseUrl}/cycles/completed`, { withCredentials: true })
      .pipe(map((res) => (Array.isArray(res) ? res : (res?.data ?? []))));
  }

  /**
   * AC-1: the paginated HR workspace for a cycle — employee rows + budget tracker +
   * export availability. The filters (type/status/search/department/sort) and paging
   * are passed through as query params; the wire format lives here only.
   */
  getWorkspace(
    cycleId: string,
    opts: {
      type?: string;
      status?: string;
      search?: string;
      departmentId?: string;
      sort?: string;
      dir?: 'asc' | 'desc';
      page?: number;
      pageSize?: number;
    } = {},
  ): Observable<IRecommendationWorkspace> {
    let params = new HttpParams().set('cycleId', cycleId);
    if (opts.type) params = params.set('type', opts.type);
    if (opts.status) params = params.set('status', opts.status);
    if (opts.search) params = params.set('search', opts.search);
    if (opts.departmentId) params = params.set('departmentId', opts.departmentId);
    if (opts.sort) params = params.set('sort', opts.sort);
    if (opts.dir) params = params.set('dir', opts.dir);
    if (opts.page != null) params = params.set('page', String(opts.page));
    if (opts.pageSize != null)
      params = params.set('pageSize', String(opts.pageSize));
    return this.http
      .get<RecommendationWorkspaceWire>(`${this.baseUrl}/workspace`, {
        params,
        withCredentials: true,
      })
      .pipe(map(mapRecommendationWorkspace));
  }

  /**
   * AC-2/FR-2: run the rating-threshold rules. `preview=true` (default, BR-3) returns
   * the proposed changes WITHOUT persisting; `preview=false` persists draft
   * recommendations. The FE shows the preview, then re-calls with preview=false on
   * "Apply".
   */
  autoGenerate(
    request: IAutoGenerateRequest,
    preview = true,
  ): Observable<IAutoGeneratePreview> {
    const params = new HttpParams().set('preview', String(preview));
    return this.http.post<IAutoGeneratePreview>(
      `${this.baseUrl}/auto-generate`,
      request,
      { params, withCredentials: true },
    );
  }

  /**
   * §8 inline edit / FR-3 manual override. An override is the `POST recommendations`
   * upsert (RecommendationController.Save) keyed by employee + cycle, NOT a
   * PUT /{recommendationId}. The flat editor amount/percentage map to the
   * type-specific bonus/increment fields of `SaveRecommendationRequest.Details`. The
   * server re-validates the mandatory-justification rule and recomputes the budget;
   * returns the updated row.
   */
  updateRecommendation(
    employeeId: string,
    cycleId: string,
    request: IUpdateRecommendationRequest,
  ): Observable<IRecommendationRow> {
    const isBonus = request.type === 'Bonus';
    const isIncrement = request.type === 'Increment';
    const body = {
      employeeId,
      cycleId,
      type: request.type,
      details: {
        targetGrade: request.targetGrade,
        targetTitle: request.targetTitle,
        effectiveDate: request.effectiveDate,
        bonusAmount: isBonus ? request.amount : null,
        bonusPercent: isBonus ? request.percentage : null,
        incrementAmount: isIncrement ? request.amount : null,
        incrementPercent: isIncrement ? request.percentage : null,
        trainingCourse: request.trainingCourse,
        customTypeLabel: null,
        budgetId: null,
      },
      justification: request.justification,
    };
    return this.http.post<IRecommendationRow>(this.baseUrl, body, {
      withCredentials: true,
    });
  }

  /** AC-3: submit a finalized recommendation into the approval workflow (FR-4). */
  submit(
    recommendationId: string,
    comment?: string,
  ): Observable<IRecommendationRow> {
    return this.http.post<IRecommendationRow>(
      `${this.baseUrl}/${recommendationId}/submit`,
      { comment: comment ?? null },
      { withCredentials: true },
    );
  }

  /** Approver decision (status → Approved/Rejected). Approver-gated server-side. */
  decide(
    recommendationId: string,
    decision: 'Approve' | 'Reject',
    comment?: string,
  ): Observable<IRecommendationRow> {
    // The backend has separate approve/reject routes (not a single /decision
    // endpoint); the decision selects the path and the body carries only the comment.
    const action = decision === 'Approve' ? 'approve' : 'reject';
    return this.http.post<IRecommendationRow>(
      `${this.baseUrl}/${recommendationId}/${action}`,
      { comment: comment ?? null },
      { withCredentials: true },
    );
  }

  /** FR-8/BR-4: re-fetch the budget tracker after an edit/submit. */
  getBudget(cycleId: string): Observable<IBudgetTracker> {
    const params = new HttpParams().set('cycleId', cycleId);
    return this.http.get<IBudgetTracker>(`${this.baseUrl}/budget`, {
      params,
      withCredentials: true,
    });
  }

  /** AC-4/FR-6: the aggregate summary (promotions, bonus pool, dept distribution). */
  getSummary(cycleId: string): Observable<IRecommendationSummary> {
    const params = new HttpParams().set('cycleId', cycleId);
    return this.http.get<IRecommendationSummary>(`${this.baseUrl}/summary`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * AC-5: a MANAGER's direct reports only (final score + recommendation status).
   * There is no dedicated `/team` route — a manager uses the workspace GET, which the
   * backend hard-scopes to direct reports server-side (Performance.Review.Team). We
   * return just the row page so the manager list stays a simple array.
   */
  getTeamRecommendations(cycleId: string): Observable<IRecommendationRow[]> {
    const params = new HttpParams().set('cycleId', cycleId);
    return this.http
      .get<RecommendationWorkspaceWire>(`${this.baseUrl}/workspace`, {
        params,
        withCredentials: true,
      })
      .pipe(map((ws) => mapRecommendationWorkspace(ws).page.rows));
  }

  /**
   * FR-6: download the server-generated recommendation summary export. Returns the
   * full HttpResponse so the caller can read the Content-Disposition filename. Only
   * formats reported in `availableExportFormats` should be requested.
   */
  export(
    format: RecommendationExportFormat,
    cycleId: string,
  ): Observable<HttpResponse<Blob>> {
    const params = new HttpParams()
      .set('format', format)
      .set('cycleId', cycleId);
    // The backend export route is GET recommendations/summary/export (ExportSummary).
    return this.http.get(`${this.baseUrl}/summary/export`, {
      params,
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }
}
