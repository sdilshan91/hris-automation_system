import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IPortalApplication,
  IPortalDashboard,
  IPortalInterview,
  IPortalInterviewer,
  IPortalOffer,
  IPortalTimelineEvent,
} from '../models/portal.models';
import { OfferResponse, OfferStatus, SalaryFrequency } from '../models/offer.models';
import { ApplicantStage } from '../models/applicant.models';
import { InterviewType } from '../models/interview.models';

/**
 * US-REC-008: Service for the ANONYMOUS candidate portal (magic link).
 *
 * Every call is anonymous (NO `withCredentials`) and carries the magic-link token
 * in the `X-Portal-Token` HEADER (kept out of the URL/query so it never lands in
 * server logs or browser history) — the backend resolves the applicant + tenant
 * from it (NFR-3 / NFR-4). Tenant scoping is also enforced by the subdomain via the
 * tenantInterceptor + backend RLS (AC-4 / BR-4). Responses are the BARE `T`; the
 * apiEnvelopeInterceptor (US-PLT-001) unwraps the envelope, so the service never
 * touches `.data`.
 *
 * The backend DTO ↔ FE shape mapping lives in ONE place here (`mapDashboard` and
 * friends) so a field/path mismatch is a one-line fix. See `portal.models.ts` for
 * the full proposed contract.
 */
/** Header carrying the magic-link token (matches the backend `X-Portal-Token`). */
const PORTAL_TOKEN_HEADER = 'X-Portal-Token';

@Injectable({ providedIn: 'root' })
export class PortalService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/careers/portal`;

  /** Build the request headers carrying the magic-link token. */
  private tokenHeaders(token: string): HttpHeaders {
    return new HttpHeaders({ [PORTAL_TOKEN_HEADER]: token });
  }

  // ─── Dashboard (AC-1 / FR-2) ─────────────────────────────

  /** Load the candidate's applications dashboard for a magic-link token. */
  getDashboard(token: string): Observable<IPortalDashboard> {
    return this.http
      .get<unknown>(`${this.base}/dashboard`, {
        headers: this.tokenHeaders(token),
      })
      .pipe(map((res) => this.mapDashboard(res)));
  }

  // ─── Offer PDF (FR-4) ────────────────────────────────────

  /**
   * Download the offer letter PDF as a blob, streamed through the API with the
   * token (FR-4). Returns the response so the caller can read the
   * Content-Disposition filename. Anonymous — no credentials.
   */
  downloadOfferDocument(
    offerId: string,
    token: string,
  ): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/offers/${offerId}/document`, {
      headers: this.tokenHeaders(token),
      responseType: 'blob',
      observe: 'response',
    });
  }

  // ─── Respond to offer (AC-3 / FR-5 / BR-2) ───────────────

  /**
   * Record the candidate's one-time response to an active offer (AC-3 / BR-2). The
   * backend body is `{ accept: boolean }`. On Accept the backend advances the
   * applicant to `Hired` (BR-3); the caller reloads the dashboard to reflect it.
   */
  respondToOffer(
    offerId: string,
    token: string,
    response: OfferResponse,
  ): Observable<IPortalOffer> {
    return this.http
      .post<unknown>(
        `${this.base}/offers/${offerId}/respond`,
        { accept: response === 'Accepted' },
        { headers: this.tokenHeaders(token) },
      )
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── Request a new link (FR-8 / BR-5) ────────────────────

  /**
   * Request a fresh magic link by email (FR-8 / BR-5). The backend verifies the
   * email matches an existing application and (when so) emails a new link. To
   * avoid user enumeration the backend should respond 2xx regardless; the FE shows
   * a generic "check your email" confirmation.
   */
  requestLink(email: string): Observable<void> {
    return this.http
      .post<unknown>(`${this.base}/request-link`, { email })
      .pipe(map(() => undefined));
  }

  // ─── Token classification ────────────────────────────────

  /**
   * True when an error means the magic link is expired/invalid (FR-8 / BR-5) — the
   * candidate should be shown the "request a new link" prompt. The backend returns
   * 401 (invalid signature/applicant) or 410 (expired token) for these.
   */
  static isTokenError(err: HttpErrorResponse): boolean {
    return err.status === 401 || err.status === 410;
  }

  // ─── Mapping (ONE place — reconcile with backend here) ───

  private mapDashboard(res: unknown): IPortalDashboard {
    const src = (res ?? {}) as Partial<IPortalDashboard>;
    const rows = Array.isArray(src.applications) ? src.applications : [];
    return {
      applicantName: src.applicantName ?? '',
      applicantEmail: src.applicantEmail ?? '',
      applications: rows.map((a) => this.mapApplication(a)),
    };
  }

  private mapApplication(res: unknown): IPortalApplication {
    const src = (res ?? {}) as Record<string, unknown>;
    // Backend dashboard DTO uses currentStage / upcomingInterview / activeOffer;
    // tolerate the simpler names too so the mapping survives either DTO shape.
    const stage = (src['currentStage'] ?? src['stage'] ?? 'Applied') as ApplicantStage;
    const interview = src['upcomingInterview'] ?? src['interview'];
    const offer = src['activeOffer'] ?? src['offer'];
    const timeline = src['timeline'];
    return {
      id: (src['id'] as string) ?? '',
      applicationReferenceNumber:
        (src['applicationReferenceNumber'] as string) ?? '',
      vacancyId: (src['vacancyId'] as string) ?? '',
      vacancyTitle: (src['vacancyTitle'] as string) ?? '',
      departmentName: (src['departmentName'] as string) ?? null,
      appliedAt: (src['appliedAt'] as string) ?? '',
      stage,
      interview: interview ? this.mapInterview(interview) : null,
      offer: offer ? this.mapOffer(offer) : null,
      timeline: Array.isArray(timeline)
        ? timeline.map((t) => this.mapTimelineEvent(t))
        : [],
    };
  }

  private mapInterview(res: unknown): IPortalInterview {
    const src = (res ?? {}) as Record<string, unknown>;
    // Backend sends interviewerNames: string[]; tolerate an interviewers:[{name}] shape.
    const names = src['interviewerNames'];
    const objs = src['interviewers'];
    let interviewers: IPortalInterviewer[] = [];
    if (Array.isArray(names)) {
      interviewers = names.map((n) => ({ name: (n as string) ?? '' }));
    } else if (Array.isArray(objs)) {
      interviewers = objs.map((i) => ({
        name: (i as IPortalInterviewer)?.name ?? '',
      }));
    }
    return {
      id: (src['id'] as string) ?? '',
      roundNumber: (src['roundNumber'] as number) ?? 1,
      interviewType: (src['interviewType'] ?? 'Video') as InterviewType,
      scheduledDate: (src['scheduledDate'] as string) ?? '',
      startTime: this.normalizeTime(src['startTime'] as string),
      durationMinutes: (src['durationMinutes'] as number) ?? 0,
      location: (src['location'] as string) ?? null,
      videoLink: (src['videoLink'] as string) ?? null,
      interviewers,
    };
  }

  private mapOffer(res: unknown): IPortalOffer {
    const src = (res ?? {}) as Record<string, unknown>;
    return {
      id: (src['id'] as string) ?? '',
      offerReferenceNumber: (src['offerReferenceNumber'] as string) ?? '',
      status: (src['status'] ?? 'Sent') as OfferStatus,
      offeredPosition: (src['offeredPosition'] as string) ?? '',
      departmentName: (src['departmentName'] as string) ?? null,
      salaryAmount: (src['salaryAmount'] as number) ?? 0,
      currency: (src['currency'] as string) ?? '',
      salaryFrequency: (src['salaryFrequency'] ?? 'Annual') as SalaryFrequency,
      benefitsSummary: (src['benefitsSummary'] as string) ?? null,
      startDate: (src['startDate'] as string) ?? '',
      expiryDate: (src['expiryDate'] as string) ?? '',
      // Backend uses canRespond + isDocumentAvailable; tolerate hasDocument.
      canRespond: (src['canRespond'] as boolean) ?? false,
      hasDocument:
        (src['isDocumentAvailable'] as boolean) ??
        (src['hasDocument'] as boolean) ??
        false,
    };
  }

  private mapTimelineEvent(res: unknown): IPortalTimelineEvent {
    const src = (res ?? {}) as Record<string, unknown>;
    // Backend uses occurredAt; tolerate at.
    return {
      at: (src['occurredAt'] as string) ?? (src['at'] as string) ?? '',
      label: (src['label'] as string) ?? '',
    };
  }

  /**
   * Normalize a time value to HH:mm. The backend serializes `TimeOnly` as
   * "HH:mm:ss" (or "HH:mm:ss.fff"); the cards display HH:mm.
   */
  private normalizeTime(time: string | null | undefined): string {
    if (!time) {
      return '';
    }
    const m = /^(\d{2}:\d{2})/.exec(time);
    return m ? m[1] : time;
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Parse the filename from a Content-Disposition header (PDF download). */
  static filenameFromResponse(
    res: HttpResponse<Blob>,
    fallback: string,
  ): string {
    const cd = res.headers.get('content-disposition') ?? '';
    const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(cd);
    if (utf8?.[1]) {
      return decodeURIComponent(utf8[1].trim());
    }
    const plain = /filename="?([^";]+)"?/i.exec(cd);
    if (plain?.[1]) {
      return plain[1].trim();
    }
    return fallback;
  }

  /** Parse an error body into a human-readable message; caller shows verbatim. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred. Please try again.';
  }
}
