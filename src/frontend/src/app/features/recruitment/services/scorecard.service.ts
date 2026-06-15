import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IScorecard,
  IScorecardBoard,
  IScorecardCriterion,
  IScorecardRequest,
  ICriterionRating,
  RatingScore,
} from '../models/scorecard.models';

/**
 * US-REC-006: Service for the interview scorecard — fetching tenant criteria,
 * submitting / editing a scorecard, and the recruiter's consolidated view.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-4 /
 * NFR-2). Responses are the BARE `T` — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the envelope, so this service never touches `.data`.
 *
 * The backend DTO ↔ FE shape mapping lives in ONE place here so a field/path
 * mismatch is a one-line fix.
 *
 * CONTRACT (assumed — reconcile with backend; `apiBaseUrl` already includes
 * `/api/v1`):
 *   GET  /recruitment/scorecards/criteria                -> IScorecardCriterion[]
 *   GET  /recruitment/interviews/:interviewId/scorecards -> IScorecardBoard
 *   POST /recruitment/interviews/:interviewId/scorecards  body IScorecardRequest -> IScorecard
 *   PUT  /recruitment/scorecards/:id                      body IScorecardRequest -> IScorecard
 *
 * Anti-bias (FR-6 / BR-5) is enforced server-side: the board response sets
 * `canViewOthers=false` and omits other interviewers' cards until the current
 * interviewer submits. The FE only reflects the flag.
 */
@Injectable({ providedIn: 'root' })
export class ScorecardService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/recruitment`;

  // ─── Criteria (FR-1 / BR-2) ──────────────────────────────

  /** Tenant-configured evaluation criteria (or defaults), sorted for display. */
  listCriteria(): Observable<IScorecardCriterion[]> {
    return this.http
      .get<
        IScorecardCriterion[]
      >(`${this.base}/scorecard-criteria`, { withCredentials: true })
      .pipe(map((rows) => (rows ?? []).map((c) => this.mapCriterion(c))));
  }

  // ─── Consolidated view (AC-2 / AC-3 / FR-6) ──────────────

  /**
   * All scorecards for an interview the caller may see, plus the aggregate average
   * and the anti-bias flags. The backend gates `scorecards`/`canViewOthers` (BR-5).
   */
  listForInterview(interviewId: string): Observable<IScorecardBoard> {
    return this.http
      .get<unknown>(`${this.base}/interviews/${interviewId}/scorecards`, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapBoard(interviewId, res)));
  }

  // ─── Submit / edit (AC-1 / FR-1 / FR-2) ──────────────────

  /** Submit a new scorecard for an interview (AC-1). Interviewer identity is server-side (BR-1). */
  submit(
    interviewId: string,
    request: IScorecardRequest,
  ): Observable<IScorecard> {
    return this.http
      .post<unknown>(
        `${this.base}/interviews/${interviewId}/scorecard`,
        request,
        { withCredentials: true },
      )
      .pipe(map((res) => this.mapScorecard(res)));
  }

  /**
   * Edit an existing scorecard within the lock window (FR-2 / BR-4). The backend has
   * no separate PUT — the same POST /interviews/{interviewId}/scorecard upserts the
   * caller's scorecard — so this routes through {@link submit}.
   */
  update(interviewId: string, request: IScorecardRequest): Observable<IScorecard> {
    return this.submit(interviewId, request);
  }

  // ─── Mapping (ONE place — reconcile with backend here) ───

  private mapCriterion(
    c: Partial<IScorecardCriterion> | null | undefined,
  ): IScorecardCriterion {
    const src = c ?? {};
    return {
      key: src.key ?? '',
      label: src.label ?? '',
    };
  }

  private mapRating(
    r: Partial<ICriterionRating> | null | undefined,
  ): ICriterionRating {
    const src = r ?? {};
    return {
      criterionKey: src.criterionKey ?? '',
      criterionLabel: src.criterionLabel ?? null,
      score: (src.score ?? 0) as RatingScore,
      comment: src.comment ?? null,
    };
  }

  /**
   * Map a backend scorecard DTO to `IScorecard`. Tolerates `ratings` absent and
   * `interviewerName` falling back to a joined `fullName`. Keep aligned with the
   * backend `ScorecardDto`; a field rename is a one-line change here.
   */
  private mapScorecard(r: unknown): IScorecard {
    const src = (r ?? {}) as Partial<IScorecard> & { fullName?: string };
    return {
      id: src.id ?? '',
      interviewId: src.interviewId ?? '',
      interviewerEmployeeId: src.interviewerEmployeeId ?? '',
      interviewerName: src.interviewerName ?? src.fullName ?? '',
      overallRecommendation: src.overallRecommendation ?? 'NoHire',
      overallRecommendationName: src.overallRecommendationName ?? null,
      ratings: (src.ratings ?? []).map((x) => this.mapRating(x)),
      generalNotes: src.generalNotes ?? null,
      averageScore: src.averageScore ?? 0,
      submittedAt: src.submittedAt ?? '',
      lockedAt: src.lockedAt ?? null,
      isLocked: src.isLocked ?? false,
    };
  }

  /**
   * Normalize the consolidated board. Tolerates the backend returning either an
   * `IScorecardBoard` envelope or a bare `IScorecard[]` (then the FE computes a
   * fallback aggregate and assumes no anti-bias gate). Defaults `canViewOthers`
   * to true so a plain recruiter view is never accidentally hidden.
   */
  private mapBoard(_interviewId: string, res: unknown): IScorecardBoard {
    if (Array.isArray(res)) {
      const cards = res.map((x) => this.mapScorecard(x));
      return {
        scorecards: cards,
        aggregateAverageScore: this.fallbackAggregate(cards),
        hiddenCount: 0,
        antiBiasApplies: false,
      };
    }
    const obj = (res ?? {}) as Partial<IScorecardBoard>;
    const cards = (obj.scorecards ?? []).map((x) => this.mapScorecard(x));
    return {
      scorecards: cards,
      aggregateAverageScore:
        obj.aggregateAverageScore ?? this.fallbackAggregate(cards),
      hiddenCount: obj.hiddenCount ?? 0,
      antiBiasApplies: obj.antiBiasApplies ?? false,
    };
  }

  /** Mean of the cards' average scores when the backend omits the aggregate. */
  private fallbackAggregate(cards: IScorecard[]): number | null {
    if (cards.length === 0) {
      return null;
    }
    const sum = cards.reduce((a, c) => a + (c.averageScore ?? 0), 0);
    return Math.round((sum / cards.length) * 10) / 10;
  }

  /** Parse an error body into a human-readable message; caller shows verbatim. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred.';
  }
}
