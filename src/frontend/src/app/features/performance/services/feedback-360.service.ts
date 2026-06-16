import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ICompletionTracker,
  IFeedback360Results,
  IFeedbackForm,
  IReviewerConfig,
  ISaveReviewersRequest,
  ISubmitFeedbackRequest,
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
 * results payload omits reviewer identifiers entirely; this service does nothing to
 * reconstruct identity — it returns exactly what the API sends.
 */
@Injectable({ providedIn: 'root' })
export class Feedback360Service {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/performance/feedback-360`;

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
   * per-category comparison, composite score (FR-6), and individual comments. When
   * anonymity is on (FR-5/NFR-3) the payload OMITS reviewer identifiers — the service
   * returns exactly what the API sends.
   */
  getResults(employeeId: string): Observable<IFeedback360Results> {
    return this.http.get<IFeedback360Results>(
      `${this.baseUrl}/employees/${employeeId}/results`,
      { withCredentials: true },
    );
  }

  /**
   * FR-7: download the server-generated PDF summary (OPTIONAL endpoint — only wired
   * when `IFeedback360Results.exportAvailable` is true). The backend owns PDF
   * generation + tenant branding; the FE just triggers the download. Returns the full
   * HttpResponse so the caller can read the Content-Disposition filename.
   */
  exportResultsPdf(employeeId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(
      `${this.baseUrl}/employees/${employeeId}/results/export`,
      { withCredentials: true, responseType: 'blob', observe: 'response' },
    );
  }
}
