import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
  HttpResponse,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApplicantStage } from '../models/applicant.models';
import {
  IApplicantDetail,
  IPipelineBoard,
  IPipelineFilters,
  IStageChangeRequest,
  IStageMoveResult,
  IStageTransition,
  PIPELINE_STAGES,
} from '../models/pipeline.models';

/**
 * US-REC-003: Service for the recruiter applicant pipeline (Kanban) — board read,
 * applicant detail (+ stage history), stage transitions, and resume download.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 /
 * NFR-3). Responses are the BARE `T` — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the envelope, so this service never touches `.data`.
 *
 * CONTRACT (assumed — reconcile with backend; mapping kept in ONE place here):
 *   GET  /recruitment/vacancies/:vacancyId/pipeline?stage=&source=&from=&to=&search=
 *        -> IPipelineBoard
 *   GET  /recruitment/applicants/:id            -> IApplicantDetail
 *   POST /recruitment/applicants/:id/stage      body IStageChangeRequest -> IApplicantCard
 *   GET  /recruitment/applicants/:id/resume     -> Blob (Content-Disposition filename)
 */
@Injectable({ providedIn: 'root' })
export class PipelineService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/recruitment`;

  // ─── Board (AC-1 / FR-1 / FR-5 / FR-6) ───────────────────

  /** Kanban board for a vacancy with optional filters (FR-6). */
  getPipeline(
    vacancyId: string,
    filters: IPipelineFilters = {},
  ): Observable<IPipelineBoard> {
    let params = new HttpParams();
    if (filters.stage) {
      params = params.set('stage', filters.stage);
    }
    if (filters.source) {
      params = params.set('source', filters.source);
    }
    if (filters.from) {
      params = params.set('from', filters.from);
    }
    if (filters.to) {
      params = params.set('to', filters.to);
    }
    if (filters.search) {
      params = params.set('search', filters.search);
    }
    return this.http
      .get<IPipelineBoard>(`${this.base}/vacancies/${vacancyId}/pipeline`, {
        withCredentials: true,
        params,
      })
      .pipe(map((res) => this.normalizeBoard(res, vacancyId)));
  }

  // ─── Applicant detail (AC-3 / FR-7) ──────────────────────

  /** Full applicant + stage-transition history for the detail slide-over. */
  getApplicant(id: string): Observable<IApplicantDetail> {
    // Backend returns ApplicantDetailDto { profile, stageHistory, resumeFileName, ... };
    // flatten the profile into the shape the slide-over consumes.
    return this.http
      .get<{
        profile: Omit<IApplicantDetail, 'stageHistory'>;
        stageHistory?: IStageTransition[];
      }>(`${this.base}/applicants/${id}/detail`, { withCredentials: true })
      .pipe(
        map((d) => ({ ...d.profile, stageHistory: d?.stageHistory ?? [] })),
      );
  }

  // ─── Stage transition (AC-2 / FR-3 / BR-3 / BR-4 / BR-5) ──

  /**
   * Move an applicant to a new stage. Backend records the audit trail + history
   * (BR-5) and enforces reason rules (BR-3/BR-4) authoritatively; the FE prompts
   * up front for UX.
   *
   * The gates are SOFT (US-REC-004 / §10): the move still succeeds when headcount
   * is filled or gate criteria fail — the backend returns `warnings[]` for the
   * recruiter to acknowledge (BR-4 / FR-1). A HARD failure (vacancy closed or
   * cancelled, FR-8) comes back as an HTTP error and is surfaced by the caller.
   *
   * Reconciles the backend `MoveApplicantStageResultDto` shape to `IStageMoveResult`
   * in ONE place here. Tolerates `warnings` being absent (defaults to `[]`).
   */
  changeStage(
    id: string,
    request: IStageChangeRequest,
  ): Observable<IStageMoveResult> {
    return this.http
      .post<{
        applicantId: string;
        toStage: ApplicantStage;
        enteredStageAt?: string | null;
        warnings?: string[] | null;
      }>(`${this.base}/applicants/${id}/move-stage`, request, {
        withCredentials: true,
      })
      .pipe(
        map((r) => ({
          id: r.applicantId,
          stage: r.toStage,
          enteredStageAt: r.enteredStageAt ?? null,
          warnings: r.warnings ?? [],
        })),
      );
  }

  // ─── Resume download (FR-7 / NFR-5) ──────────────────────

  /**
   * Download the applicant resume as a blob. We deliberately stream the file
   * through the API (with auth) rather than exposing a direct blob-storage URL
   * (NFR-5). Returns the response so the caller can read the Content-Disposition
   * filename.
   */
  downloadResume(id: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/applicants/${id}/resume`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  // ─── Helpers ─────────────────────────────────────────────

  /**
   * Ensure every canonical stage column exists (FR-1: one column per stage even
   * when empty) and the board has a vacancyId + total. Tolerates a backend that
   * only returns non-empty stages, or extra custom stages (§10) which are
   * appended after the canonical set.
   */
  private normalizeBoard(
    res: IPipelineBoard,
    vacancyId: string,
  ): IPipelineBoard {
    const incoming = res?.stages ?? [];
    const byStage = new Map(incoming.map((s) => [s.stage, s]));

    const ordered = PIPELINE_STAGES.map((stage) => {
      const existing = byStage.get(stage);
      return existing
        ? {
            stage,
            applicants: existing.applicants ?? [],
            count: existing.count ?? (existing.applicants?.length ?? 0),
          }
        : { stage, applicants: [], count: 0 };
    });

    // Append any non-canonical custom stages the backend may send (§10).
    for (const s of incoming) {
      if (!PIPELINE_STAGES.includes(s.stage)) {
        ordered.push({
          stage: s.stage,
          applicants: s.applicants ?? [],
          count: s.count ?? (s.applicants?.length ?? 0),
        });
      }
    }

    const total =
      res?.total ?? ordered.reduce((sum, s) => sum + s.applicants.length, 0);

    return {
      vacancyId: res?.vacancyId ?? vacancyId,
      vacancyTitle: res?.vacancyTitle ?? null,
      stages: ordered,
      total,
    };
  }

  /** Parse the filename from a Content-Disposition header (resume download). */
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
