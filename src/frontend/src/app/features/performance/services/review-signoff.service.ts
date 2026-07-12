import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, switchMap } from 'rxjs/operators';
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
 * BUG-243: reconciled to the real ReviewSignoffController routes, all keyed by
 * `reviews/cycles/{cycleId}/employees/{employeeId}/…`. Each method takes the
 * `employeeId` (the manager workspace holds it) and resolves the active cycleId INSIDE
 * this service via `GET .../cycles/active` + `switchMap`. The notes body is mapped to
 * the backend `SaveMeetingNotesRequest.Body` field and dispute-resolution to
 * `ResolveDisputeRequest.{Amend,Comments}` here, so the components stay unchanged.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see review-signoff.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class ReviewSignoffService {
  private readonly http = inject(HttpClient);
  private readonly perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  private readonly reviewsBase = `${this.perfBase}/reviews`;

  /** Resolve the active cycle's id (CyclesController.GetActive admits Review.Team/Self). */
  private activeCycleId(): Observable<string> {
    return this.http
      .get<{ id: string }>(`${this.perfBase}/cycles/active`, {
        withCredentials: true,
      })
      .pipe(map((c) => c.id));
  }

  /** Base path for an employee's sign-off record in the active cycle. */
  private notesBase(cycleId: string, employeeId: string): string {
    return `${this.reviewsBase}/cycles/${cycleId}/employees/${employeeId}`;
  }

  // ── Employee self-service (ISSUE-288) ──────────────────────────────────────
  // Caller-scoped endpoints for the EMPLOYEE self view: the backend resolves the
  // caller's OWN employeeId (from the session) + the active cycleId server-side, so
  // these take NO cycleId/employeeId params. They return the SAME IReviewSignoff
  // shape as the manager endpoints and are authorized `Performance.Read.Self`.
  // The `active/me` segment is literal (no ambiguity with `{cycleId}/employees/…`).

  /** Load the caller's OWN sign-off record in the active cycle (AC-3/AC-4). */
  getMySignoff(): Observable<IReviewSignoff> {
    return this.http.get<IReviewSignoff>(
      `${this.reviewsBase}/cycles/active/me/notes`,
      { withCredentials: true },
    );
  }

  /**
   * AC-3: the caller acknowledges & signs their own review. The server records the
   * signature (name+timestamp+IP) and flips status to SignedOff. Empty body.
   */
  acknowledgeMy(): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.reviewsBase}/cycles/active/me/acknowledge`,
      {},
      { withCredentials: true },
    );
  }

  /**
   * AC-3/FR-4: the caller disputes their own review with mandatory comments. The
   * server records the dispute, flips status to Disputed, notifies manager + HR.
   */
  disputeMy(comments: string): Observable<IReviewSignoff> {
    return this.http.post<IReviewSignoff>(
      `${this.reviewsBase}/cycles/active/me/dispute`,
      { comments },
      { withCredentials: true },
    );
  }

  /** Load the full sign-off record for an employee (drives every US-PRF-006 screen). */
  getSignoff(employeeId: string): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/notes`,
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * Save/update meeting notes (BR-1: only while NotesDraft; the server re-checks).
   * Returns the persisted record. Status is unchanged.
   */
  saveNotes(
    employeeId: string,
    request: ISaveMeetingNotesRequest,
  ): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.put<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/notes`,
          { body: request.meetingNotesHtml },
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * AC-2: persist the notes, record the manager signature, flip status to
   * PendingEmployeeSignOff, notify the employee. Returns the updated record.
   */
  requestSignoff(
    employeeId: string,
    request: ISaveMeetingNotesRequest,
  ): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.post<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/request-signoff`,
          { body: request.meetingNotesHtml },
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * AC-3: employee acknowledges & signs. The server records the signature
   * (name+timestamp+IP) and flips status to SignedOff. No body.
   */
  acknowledge(employeeId: string): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.post<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/acknowledge`,
          {},
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * AC-3/FR-4: employee disputes with mandatory comments. The server records the
   * dispute, flips status to Disputed, notifies the manager + HR (FR-5).
   */
  dispute(
    employeeId: string,
    request: IDisputeRequest,
  ): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.post<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/dispute`,
          { comments: request.comments },
          { withCredentials: true },
        ),
      ),
    );
  }

  /** BR-4: HR resolves a dispute (Amend reopens notes; Confirm upholds the review). */
  resolveDispute(
    employeeId: string,
    request: IResolveDisputeRequest,
  ): Observable<IReviewSignoff> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.post<IReviewSignoff>(
          `${this.notesBase(cycleId, employeeId)}/resolve-dispute`,
          { amend: request.resolution === 'Amend', comments: request.note ?? null },
          { withCredentials: true },
        ),
      ),
    );
  }

  /**
   * FR-6 (OPTIONAL): download the server-generated PDF of the completed review.
   * Returns the raw HttpResponse so the caller can read the Content-Disposition
   * filename. Wired only when `record.exportAvailable` is true.
   */
  exportPdf(employeeId: string): Observable<HttpResponse<Blob>> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http.get(`${this.notesBase(cycleId, employeeId)}/export`, {
          responseType: 'blob',
          observe: 'response',
          withCredentials: true,
        }),
      ),
    );
  }
}
