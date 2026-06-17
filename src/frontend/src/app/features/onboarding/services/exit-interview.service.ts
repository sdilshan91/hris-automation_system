import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IExitInterview,
  IExitInterviewAnalytics,
  IExitInterviewTemplate,
  IRecordExitInterviewRequest,
} from '../models/exit-interview.models';

/**
 * US-ONB-006: Exit Interview Recording service.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 / NFR-2).
 * Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the ApiResponse<T>
 * envelope, so this service never touches `.data` (matches OffboardingService and
 * every other FE feature service in this repo).
 *
 * PINNED CONTRACT (backend builds these EXACTLY — do not drift):
 *   GET  /exit-interviews/template                       -> IExitInterviewTemplate
 *   GET  /exit-interviews?offboardingId={id}             -> IExitInterview (or 404)
 *   POST /exit-interviews                                 -> IExitInterview
 *   GET  /exit-interviews/analytics?fromDate=&toDate=    -> IExitInterviewAnalytics
 */
@Injectable({ providedIn: 'root' })
export class ExitInterviewService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/exit-interviews`;

  /**
   * Load the tenant's configurable exit-interview template grouped by category
   * (AC-1 / FR-1). Used to render the questionnaire for both interview modes.
   */
  getTemplate(): Observable<IExitInterviewTemplate> {
    return this.http.get<IExitInterviewTemplate>(`${this.base}/template`, {
      withCredentials: true,
    });
  }

  /**
   * The existing exit interview for an offboarding instance, if one was already
   * recorded (404 otherwise) — supports BR-1 (one interview per offboarding) so
   * the form can show the immutable submitted state instead of re-collecting.
   */
  getByOffboarding(offboardingId: string): Observable<IExitInterview> {
    return this.http.get<IExitInterview>(this.base, {
      params: { offboardingId },
      withCredentials: true,
    });
  }

  /**
   * Persist the questionnaire responses against the offboarding record (AC-2/AC-3).
   * `tenant_id` + `conducted_by` are server-set from the session (FR-6). A BR-1
   * duplicate or future-date violation comes back as a 4xx the caller surfaces.
   */
  record(
    request: IRecordExitInterviewRequest,
  ): Observable<IExitInterview> {
    return this.http.post<IExitInterview>(this.base, request, {
      withCredentials: true,
    });
  }

  /**
   * Aggregated, tenant-scoped analytics for the Exit Interview Summary (AC-4 /
   * FR-4): reason distribution, average rating per category, and trend over time.
   * Optional ISO date bounds (yyyy-MM-dd) narrow the window.
   */
  getAnalytics(
    fromDate?: string,
    toDate?: string,
  ): Observable<IExitInterviewAnalytics> {
    let params: Record<string, string> = {};
    if (fromDate) params = { ...params, fromDate };
    if (toDate) params = { ...params, toDate };
    return this.http.get<IExitInterviewAnalytics>(`${this.base}/analytics`, {
      params,
      withCredentials: true,
    });
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
