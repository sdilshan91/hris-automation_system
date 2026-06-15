import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpResponse,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IOffer,
  IOfferRequest,
  OfferResponse,
  OfferStatus,
  SalaryFrequency,
} from '../models/offer.models';

/**
 * US-REC-007: Service for generating, previewing, sending, responding to, and
 * withdrawing job offers.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 /
 * NFR-2). Responses are the BARE `T` — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the envelope, so this service never touches `.data`.
 *
 * The backend DTO ↔ FE shape mapping lives in ONE place here (`mapOffer`) so a
 * field/path mismatch is a one-line fix.
 *
 * CONTRACT (PROPOSED — no backend offer endpoints existed when this was built;
 * the backend agent should MATCH these. `apiBaseUrl` already includes `/api/v1`):
 *   POST /recruitment/applicants/:applicantId/offers  body IOfferRequest -> IOffer (Draft)
 *   GET  /recruitment/applicants/:applicantId/offers   -> IOffer[]
 *   GET  /recruitment/offers/:id                        -> IOffer
 *   GET  /recruitment/offers/:id/pdf                    -> Blob (Content-Disposition)
 *   POST /recruitment/offers/:id/send                   -> IOffer (Sent)
 *   POST /recruitment/offers/:id/respond  body { response, notes? } -> IOffer
 *   POST /recruitment/offers/:id/withdraw body { reason? }          -> IOffer (Withdrawn)
 *
 * Accept advances the applicant to the Hired stage server-side (BR-3); the FE
 * surfaces that by reloading the applicant detail after a successful respond.
 */
@Injectable({ providedIn: 'root' })
export class OfferService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/recruitment`;

  // ─── Generate (AC-1 / FR-1 / FR-2) ───────────────────────

  /** Generate a new offer (Draft) for an applicant; backend renders the PDF (FR-2). */
  generate(applicantId: string, request: IOfferRequest): Observable<IOffer> {
    // Backend route is POST /recruitment/offers with applicantId in the body.
    return this.http
      .post<unknown>(
        `${this.base}/offers`,
        { ...request, applicantId },
        { withCredentials: true },
      )
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── Read (FR-9 versions / timeline) ─────────────────────

  /** All offers for an applicant, newest first (offer history + versions, FR-9). */
  listForApplicant(applicantId: string): Observable<IOffer[]> {
    return this.http
      .get<unknown>(`${this.base}/applicants/${applicantId}/offers`, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapOfferList(res)));
  }

  /** Single offer by id. */
  getOffer(id: string): Observable<IOffer> {
    return this.http
      .get<unknown>(`${this.base}/offers/${id}`, { withCredentials: true })
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── PDF preview / download (AC-1 / FR-4) ────────────────

  /**
   * Download the generated offer PDF as a blob. We stream through the API (with
   * auth) rather than exposing a direct blob-storage URL (NFR-3). Returns the
   * response so the caller can read the Content-Disposition filename.
   */
  downloadPdf(id: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/offers/${id}/document`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  // ─── Send (AC-2 / FR-5) ──────────────────────────────────

  /** Send the offer to the applicant (Draft -> Sent); backend emails the PDF (AC-2). */
  send(id: string): Observable<IOffer> {
    return this.http
      .post<unknown>(`${this.base}/offers/${id}/send`, {}, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── Respond (AC-3 / BR-3) ───────────────────────────────

  /**
   * Record the applicant's response (Accept/Decline). On Accept the backend
   * advances the applicant to the Hired stage (BR-3); the caller reloads the
   * applicant detail to reflect it.
   */
  respond(id: string, response: OfferResponse, _notes?: string): Observable<IOffer> {
    // Backend expects { accepted: boolean } (recruiter-recorded; no notes field in Phase 1).
    const body = { accepted: response === 'Accepted' };
    return this.http
      .post<unknown>(`${this.base}/offers/${id}/respond`, body, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── Withdraw (FR-8 / BR-7) ──────────────────────────────

  /** Withdraw an offer before acceptance (FR-8); backend notifies the applicant. */
  withdraw(id: string, reason?: string): Observable<IOffer> {
    return this.http
      .post<unknown>(
        `${this.base}/offers/${id}/withdraw`,
        { reason: reason ?? null },
        { withCredentials: true },
      )
      .pipe(map((res) => this.mapOffer(res)));
  }

  // ─── Mapping (ONE place — reconcile with backend here) ───

  /**
   * Map a backend offer DTO to `IOffer`. Tolerates absent optional fields. Keep
   * aligned with the backend `OfferDto`; a field rename is a one-line change here.
   */
  private mapOffer(res: unknown): IOffer {
    const src = (res ?? {}) as Partial<IOffer>;
    return {
      id: src.id ?? '',
      offerReferenceNumber: src.offerReferenceNumber ?? '',
      applicantId: src.applicantId ?? '',
      vacancyId: src.vacancyId ?? '',
      status: (src.status ?? 'Draft') as OfferStatus,
      statusName: src.statusName ?? null,
      offeredPosition: src.offeredPosition ?? '',
      departmentId: src.departmentId ?? null,
      departmentName: src.departmentName ?? null,
      reportingManagerId: src.reportingManagerId ?? null,
      reportingManagerName: src.reportingManagerName ?? null,
      salaryAmount: src.salaryAmount ?? 0,
      currency: src.currency ?? '',
      salaryFrequency: (src.salaryFrequency ?? 'Annual') as SalaryFrequency,
      benefitsSummary: src.benefitsSummary ?? null,
      startDate: src.startDate ?? '',
      expiryDate: src.expiryDate ?? '',
      probationMonths: src.probationMonths ?? null,
      customClauses: src.customClauses ?? null,
      version: src.version ?? 1,
      sentAt: src.sentAt ?? null,
      respondedAt: src.respondedAt ?? null,
      response: src.response ?? null,
      createdAt: src.createdAt ?? '',
    };
  }

  /** Tolerate a bare array OR a `{ data }` envelope from the list endpoint. */
  private mapOfferList(res: unknown): IOffer[] {
    const rows = Array.isArray(res)
      ? res
      : ((res as { data?: unknown[] } | null)?.data ?? []);
    return (rows ?? []).map((r) => this.mapOffer(r));
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
    return 'An unexpected error occurred.';
  }
}
