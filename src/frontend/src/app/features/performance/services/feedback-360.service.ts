import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, switchMap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ICompletionTracker,
  IFeedback360ReleaseResult,
  IFeedback360Results,
  IFeedback360ResultsRaw,
  IFeedbackForm,
  IReviewerConfig,
  ISaveReviewersRequest,
  ISubmitFeedbackRequest,
  mapFeedback360Results,
} from '../models/feedback-360.models';

/**
 * US-PRF-005: Service for the 360-degree feedback experience — reviewer config,
 * feedback submit, and aggregated results. Sibling to PerformanceGoalService
 * (US-PRF-001), SelfAssessmentService (US-PRF-002), ManagerReviewService (US-PRF-003)
 * and CycleService (US-PRF-004).
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the tenant + the
 * acting user from the session and enforces `Performance.Review.All` (HR config) /
 * reviewer ownership + RLS (NFR-2/NFR-3), so the FE sends no tenant id.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see feedback-360.models.ts.
 *
 * CRITICAL (FR-5/NFR-3): anonymity is enforced server-side. When anonymity is on the
 * results payload NULLS reviewer identifiers entirely; this service does nothing to
 * reconstruct identity — `getResults()` maps the payload field-for-field and passes the
 * (already-nulled) reviewer name through untouched.
 */
@Injectable({ providedIn: 'root' })
export class Feedback360Service {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance/feedback-360`;
  private readonly perfBase = `${environment.apiBaseUrl}/tenant/performance`;

  // BUG-243: the config/reviewers/tracker/form/submit routes already match
  // Feedback360Controller (the BUG-244 employeeId-keyed + assignment-keyed endpoints).
  // Only the two cycle-keyed aggregate reads (results/report) needed reconciling — they
  // resolve the active cycleId INSIDE this service via GET .../cycles/active + switchMap.

  /** Resolve the active cycle's id (CyclesController.GetActive). */
  private activeCycleId(): Observable<string> {
    return this.http
      .get<{ id: string }>(`${this.perfBase}/cycles/active`, {
        withCredentials: true,
      })
      .pipe(map((c) => c.id));
  }

  /**
   * Reviewer-nomination screen for an employee in the active 360-enabled cycle (AC-1):
   * auto-assigned Self + Manager, suggested Peers/Direct Reports, the candidate pool,
   * the per-category minimums and the anonymity flag — one call (NFR-1).
   */
  getReviewerConfig(employeeId: string): Observable<IReviewerConfig> {
    return this.http.get<IReviewerConfig>(
      `${this.baseUrl}/employees/${employeeId}/config`,
      { withCredentials: true },
    );
  }

  /**
   * Full-replace of the manual reviewer set (Peer + Direct Report rows the HR Officer
   * added/removed, FR-2). Self + Manager are server-owned and not sent. The server
   * re-validates BR-2 (self not a peer) + per-category minimums. Returns the refreshed
   * config.
   */
  saveReviewers(
    employeeId: string,
    request: ISaveReviewersRequest,
  ): Observable<IReviewerConfig> {
    return this.http.put<IReviewerConfig>(
      `${this.baseUrl}/employees/${employeeId}/reviewers`,
      request,
      { withCredentials: true },
    );
  }

  /** Per-category submitted/pending/overdue counts for the completion tracker (AC-3). */
  getTracker(employeeId: string): Observable<ICompletionTracker> {
    return this.http.get<ICompletionTracker>(
      `${this.baseUrl}/employees/${employeeId}/tracker`,
      { withCredentials: true },
    );
  }

  /**
   * Load ONE reviewer's feedback form — the competency/goal question cards + tenant
   * rating scale (FR-4 / AC-2 deep link). The reviewer + tenant are resolved
   * server-side; RLS ensures a reviewer only loads their own assignment.
   */
  getFeedbackForm(assignmentId: string): Observable<IFeedbackForm> {
    return this.http.get<IFeedbackForm>(
      `${this.baseUrl}/assignments/${assignmentId}/form`,
      { withCredentials: true },
    );
  }

  /**
   * Submit the reviewer's feedback (AC-3). The server saves the ratings + comments,
   * marks the assignment Completed, enforces BR-3 (one submission per reviewer per
   * employee per cycle), and updates the completion tracker. Returns the locked form.
   */
  submitFeedback(
    assignmentId: string,
    request: ISubmitFeedbackRequest,
  ): Observable<IFeedbackForm> {
    return this.http.post<IFeedbackForm>(
      `${this.baseUrl}/assignments/${assignmentId}/submit`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Aggregated 360 results for an employee (AC-4): per-competency averages,
   * per-category comparison, composite score (FR-6), and individual comments. The raw
   * backend `Feedback360ResultsDto` is translated into the FE vocabulary by
   * `mapFeedback360Results()` (ISSUE-373 / GAP-012 S-1) — the wire field names
   * (`revieweeName`, `competencyAverages`, `entries`, …) never leak into the templates.
   * When anonymity is on (FR-5/NFR-3) the backend NULLS reviewer identifiers; the adapter
   * carries that through and NEVER reconstructs identity.
   */
  getResults(employeeId: string): Observable<IFeedback360Results> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get<IFeedback360ResultsRaw>(
          `${this.perfBase}/360/cycles/${cycleId}/employees/${employeeId}/results`,
          { withCredentials: true },
        ),
      ),
      map(mapFeedback360Results),
    );
  }

  /**
   * BR-4/FR-3: release the reviewee's aggregated 360 results for the active cycle so the
   * reviewee can see them. Resolves the active cycle then POSTs to the cycle-keyed release
   * route (sibling style to `getResults`/`exportResultsPdf`). The backend is the security
   * authority: it 422s `min_peer_threshold_not_met` when too few peers have submitted
   * (BR-4) and 403s `not_team_manager` for a non-owning manager. Idempotent — a re-release
   * returns 200 with the existing release row.
   */
  releaseResults(employeeId: string): Observable<IFeedback360ReleaseResult> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.post<IFeedback360ReleaseResult>(
          `${this.perfBase}/360/cycles/${cycleId}/employees/${employeeId}/release`,
          {},
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * FR-7: download the server-generated PDF summary (OPTIONAL endpoint — only wired
   * when `IFeedback360Results.exportAvailable` is true). The backend owns PDF
   * generation + tenant branding; the FE just triggers the download. Returns the full
   * HttpResponse so the caller can read the Content-Disposition filename.
   */
  exportResultsPdf(employeeId: string): Observable<HttpResponse<Blob>> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get(
          `${this.perfBase}/360/cycles/${cycleId}/employees/${employeeId}/report`,
          { withCredentials: true, responseType: 'blob', observe: 'response' },
        ),
      ),
    );
  }
}
