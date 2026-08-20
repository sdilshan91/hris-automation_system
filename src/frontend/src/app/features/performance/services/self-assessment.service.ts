import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpEvent,
  HttpEventType,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { filter, map, switchMap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAssessmentAttachment,
  ISaveSelfAssessmentRequest,
  ISelfAssessment,
  SelfAssessmentAttachmentWire,
  SelfAssessmentWire,
  mapSelfAssessment,
  mapSelfAssessmentAttachment,
} from '../models/self-assessment.models';

/**
 * US-PRF-002: Service for the EMPLOYEE self-assessment ("My Review") experience.
 *
 * Sibling to PerformanceGoalService (manager-facing goal-setting, US-PRF-001); the
 * self-assessment route strings live HERE ONLY, so a backend contract change is a
 * one-file fix. All requests use withCredentials (httpOnly cookie auth) and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The backend
 * resolves the employee + tenant from the session and enforces `Performance.Read.Self`
 * + RLS, so an employee only ever sees their OWN assessment (NFR-2) — the FE sends
 * no employee/tenant ids.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so JSON methods consume BARE payloads (US-PLT-003 enums are
 * PascalCase strings). The multipart upload observes raw events for progress and so
 * does not go through the JSON envelope.
 *
 * BUG-243: reconciled to the real SelfAssessmentController routes. `getActive()`
 * resolves the active cycleId INSIDE this service via `GET .../cycles/active`
 * (CyclesController.GetActive admits Read.Self) + `switchMap`; draft/submit carry
 * cycleId + items in the body; upload takes the cycleId. `deleteAttachment` targets
 * the real `{assessmentId}/attachments/{attachmentId}` route (BUG-244 #4).
 */

/** Progress/result events surfaced to the form during an evidence upload. */
export type IAttachmentUploadEvent =
  | { type: 'progress'; progress: number }
  | { type: 'done'; attachment: IAssessmentAttachment };

@Injectable({ providedIn: 'root' })
export class SelfAssessmentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance/self-assessments`;
  private readonly perfBase = `${environment.apiBaseUrl}/tenant/performance`;

  /** Resolve the active cycle's id (CyclesController.GetActive admits Read.Self). */
  private activeCycleId(): Observable<string> {
    return this.http
      .get<{ id: string }>(`${this.perfBase}/cycles/active`, {
        withCredentials: true,
      })
      .pipe(map((c) => c.id));
  }

  /**
   * Load the authenticated employee's self-assessment for the active cycle: the
   * assigned goals, any saved draft ratings, the rating scale, and the authoritative
   * window/lock state (AC-1 / AC-4). Resolves the active cycleId first, then loads
   * the caller's own record — one screen's worth of data (NFR-1).
   */
  getActive(): Observable<ISelfAssessment> {
    return this.activeCycleId().pipe(
      switchMap((cycleId) =>
        this.http
          .get<SelfAssessmentWire>(`${this.baseUrl}/cycles/${cycleId}/me`, {
            withCredentials: true,
          })
          .pipe(map(mapSelfAssessment)),
      ),
    );
  }

  /**
   * Persist a partial draft (AC-3 / FR-6 / NFR-3 auto-save). Status stays `Draft`;
   * no all-goals-rated gate server-side. cycleId + items travel in the body. Returns
   * the persisted assessment.
   */
  saveDraft(request: ISaveSelfAssessmentRequest): Observable<ISelfAssessment> {
    return this.http
      .put<SelfAssessmentWire>(`${this.baseUrl}/draft`, request, {
        withCredentials: true,
      })
      .pipe(map(mapSelfAssessment));
  }

  /**
   * Submit the self-assessment (AC-2). The backend re-validates all-goals-rated +
   * each comment ≥20 chars (BR-2), computes the weighted self-score (FR-4), flips
   * the status to `Submitted`, locks edits (BR-3), and notifies the manager. Rejects
   * when the window is closed (BR-1). cycleId + items travel in the body. Returns the
   * locked assessment.
   */
  submit(request: ISaveSelfAssessmentRequest): Observable<ISelfAssessment> {
    return this.http
      .post<SelfAssessmentWire>(`${this.baseUrl}/submit`, request, {
        withCredentials: true,
      })
      .pipe(map(mapSelfAssessment));
  }

  /**
   * Upload one evidence file for a goal (FR-5). Multipart with `reportProgress` so
   * the card can render a progress bar (§8). The backend virus-scans + stores in a
   * tenant-scoped path (NFR-4) and returns the created attachment row.
   */
  uploadAttachment(
    cycleId: string,
    goalId: string,
    file: File,
  ): Observable<IAttachmentUploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http
      .post<SelfAssessmentAttachmentWire>(
        `${this.baseUrl}/cycles/${cycleId}/goals/${goalId}/attachments`,
        form,
        {
          reportProgress: true,
          observe: 'events',
          withCredentials: true,
        },
      )
      .pipe(
        filter(
          (e: HttpEvent<SelfAssessmentAttachmentWire>) =>
            e.type === HttpEventType.UploadProgress ||
            e.type === HttpEventType.Response,
        ),
        map((e: HttpEvent<SelfAssessmentAttachmentWire>): IAttachmentUploadEvent => {
          if (e.type === HttpEventType.UploadProgress) {
            const progress = e.total
              ? Math.round((100 * e.loaded) / e.total)
              : 0;
            return { type: 'progress', progress };
          }
          // Response — the generated attachment wire (envelope unwrapped), mapped to
          // the view-model so `uploadedAt` → `uploadedOn` cannot silently drift.
          return {
            type: 'done',
            attachment: mapSelfAssessmentAttachment(
              (e as { body: SelfAssessmentAttachmentWire }).body,
            ),
          };
        }),
      );
  }

  /** Remove an uploaded evidence file (FR-5). */
  deleteAttachment(
    assessmentId: string,
    attachmentId: string,
  ): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/${assessmentId}/attachments/${attachmentId}`,
      { withCredentials: true },
    );
  }
}
