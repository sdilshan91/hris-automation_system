import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IDisputeRequest,
  IResolveDisputeRequest,
  IReviewSignoff,
  ISaveMeetingNotesRequest,
} from '../models/review-signoff.models';

/**
 * US-PRF-006: Service for the performance-review meeting-notes + digital sign-off
 * workflow. Sibling to ManagerReviewService (US-PRF-003). Both the manager-facing
 * (notes, request sign-off, HR resolve) and employee-facing (acknowledge, dispute)
 * operations live here — the backend authorizes each by session role/ownership.
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the acting
 * user + tenant from the session and enforces `Performance.Review.Team`/`.All` +
 * review ownership + RLS (NFR-2); sign-off records are immutable (NFR-3).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see review-signoff.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class ReviewSignoffService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/performance/sign-off`;

  /** Load the full sign-off record for a review (drives every US-PRF-006 screen). */
  getSignoff(reviewId: string): Observable<IReviewSignoff> {
    return this.http.get<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}`,
      { withCredentials: true },
    );
  }

  /**
   * Save/update meeting notes (BR-1: only while NotesDraft; the server re-checks).
   * Returns the persisted record. Status is unchanged.
   */
  saveNotes(
    reviewId: string,
    request: ISaveMeetingNotesRequest,
  ): Observable<IReviewSignoff> {
    return this.http.put<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}/notes`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * AC-2: persist the notes, record the manager signature, flip status to
   * PendingEmployeeSignOff, notify the employee. Returns the updated record.
   */
  requestSignoff(
    reviewId: string,
    request: ISaveMeetingNotesRequest,
  ): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}/request`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * AC-3: employee acknowledges & signs. The server records the signature
   * (name+timestamp+IP) and flips status to SignedOff. No body.
   */
  acknowledge(reviewId: string): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}/acknowledge`,
      {},
      { withCredentials: true },
    );
  }

  /**
   * AC-3/FR-4: employee disputes with mandatory comments. The server records the
   * dispute, flips status to Disputed, notifies the manager + HR (FR-5).
   */
  dispute(
    reviewId: string,
    request: IDisputeRequest,
  ): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}/dispute`,
      request,
      { withCredentials: true },
    );
  }

  /** BR-4: HR resolves a dispute (Amend reopens notes; Confirm upholds the review). */
  resolveDispute(
    reviewId: string,
    request: IResolveDisputeRequest,
  ): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.baseUrl}/reviews/${reviewId}/resolve`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * FR-6 (OPTIONAL): download the server-generated PDF of the completed review.
   * Returns the raw HttpResponse so the caller can read the Content-Disposition
   * filename. Wired only when `record.exportAvailable` is true.
   */
  exportPdf(reviewId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}/reviews/${reviewId}/export`, {
      responseType: 'blob',
      observe: 'response',
      withCredentials: true,
    });
  }
}
