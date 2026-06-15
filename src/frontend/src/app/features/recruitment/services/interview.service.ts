import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ILookupOption } from '../models/vacancy.models';
import {
  IInterview,
  IInterviewFilters,
  IInterviewRequest,
  IScheduleResult,
} from '../models/interview.models';

/**
 * US-REC-005: Service for interview scheduling, rescheduling, cancellation, the
 * per-applicant rounds list, and the tenant-wide agenda/calendar.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 /
 * NFR-2). Responses are the BARE `T` — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the envelope, so this service never touches `.data`.
 *
 * The schedule/reschedule endpoints return a SUCCESSFUL result that may carry
 * soft conflict `warnings[]` (interviewer double-booked, FR-7); the mapping
 * tolerates either `{ interview, warnings }` or a bare `IInterview`.
 *
 * The backend DTO ↔ FE shape mapping lives in ONE place here so a field/path
 * mismatch is a one-line fix.
 *
 * CONTRACT (assumed — reconcile with backend; `apiBaseUrl` already includes
 * `/api/v1`):
 *   POST /recruitment/applicants/:applicantId/interviews  body IInterviewRequest
 *        -> { interview, warnings? }   (FR-1 / FR-7)
 *   GET  /recruitment/applicants/:applicantId/interviews  -> IInterview[]  (AC-4)
 *   PUT  /recruitment/interviews/:id   body IInterviewRequest
 *        -> { interview, warnings? }   (AC-3 reschedule)
 *   POST /recruitment/interviews/:id/cancel  body { reason? } -> IInterview  (AC-3)
 *   GET  /recruitment/interviews?interviewerId=&vacancyId=&from=&to=&status=
 *        -> IInterview[]               (FR-5 / FR-6)
 */
@Injectable({ providedIn: 'root' })
export class InterviewService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/recruitment`;

  // ─── Per-applicant rounds (AC-4 / FR-2) ──────────────────

  /** All interviews scheduled for one applicant (every round), newest-first. */
  listForApplicant(applicantId: string): Observable<IInterview[]> {
    return this.http
      .get<
        IInterview[]
      >(`${this.base}/applicants/${applicantId}/interviews`, {
        withCredentials: true,
      })
      .pipe(map((rows) => (rows ?? []).map((r) => this.mapInterview(r))));
  }

  /** Schedule a new interview for an applicant (FR-1 / AC-1). */
  schedule(
    applicantId: string,
    request: IInterviewRequest,
  ): Observable<IScheduleResult> {
    // Backend route is POST /recruitment/interviews with applicantId in the body
    // (IInterviewRequest already carries applicantId).
    return this.http
      .post<unknown>(
        `${this.base}/interviews`,
        { ...request, applicantId },
        { withCredentials: true },
      )
      .pipe(map((res) => this.mapScheduleResult(res)));
  }

  // ─── Reschedule / cancel (AC-3) ──────────────────────────

  /** Reschedule (edit) an existing interview (AC-3). May return conflict warnings. */
  reschedule(
    id: string,
    request: IInterviewRequest,
  ): Observable<IScheduleResult> {
    return this.http
      .put<unknown>(`${this.base}/interviews/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapScheduleResult(res)));
  }

  /** Cancel an interview (AC-3). Backend notifies participants + removes the reminder. */
  cancel(id: string, reason?: string): Observable<IInterview> {
    return this.http
      .post<IInterview>(
        `${this.base}/interviews/${id}/cancel`,
        { reason: reason ?? null },
        { withCredentials: true },
      )
      .pipe(map((r) => this.mapInterview(r)));
  }

  // ─── Tenant-wide agenda / calendar (FR-5 / FR-6) ─────────

  /** All tenant interviews matching the filters, for the agenda/calendar view. */
  listInterviews(
    filters: IInterviewFilters = {},
  ): Observable<IInterview[]> {
    let params = new HttpParams();
    if (filters.interviewerId) {
      params = params.set('interviewerId', filters.interviewerId);
    }
    if (filters.vacancyId) {
      params = params.set('vacancyId', filters.vacancyId);
    }
    if (filters.from) {
      params = params.set('from', filters.from);
    }
    if (filters.to) {
      params = params.set('to', filters.to);
    }
    if (filters.status) {
      params = params.set('status', filters.status);
    }
    return this.http
      .get<IInterview[]>(`${this.base}/interviews`, {
        withCredentials: true,
        params,
      })
      .pipe(map((rows) => (rows ?? []).map((r) => this.mapInterview(r))));
  }

  // ─── Master-data lookups (form dropdowns) ────────────────

  /**
   * Searchable employee lookup for the interviewer multi-select (BR-2). Reuses the
   * Core HR endpoint (under /api/v1/tenant/...) — same as VacancyService — and
   * normalizes to ILookupOption with the department as the sublabel for the avatar
   * row (§8). Adjust ONLY this mapping if the master-data DTO differs.
   */
  searchEmployees(search?: string): Observable<ILookupOption[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    return this.http
      .get<
        Array<{
          employeeId: string;
          firstName: string;
          lastName: string;
          departmentName?: string | null;
          jobTitleName?: string | null;
        }>
      >(`${environment.apiBaseUrl}/tenant/employees`, {
        withCredentials: true,
        params,
      })
      .pipe(
        map((rows) =>
          (rows ?? []).map((e) => ({
            id: e.employeeId,
            label: `${e.firstName} ${e.lastName}`.trim(),
            sublabel: e.departmentName ?? e.jobTitleName ?? null,
          })),
        ),
      );
  }

  // ─── Mapping (ONE place — reconcile with backend here) ───

  /**
   * Map a backend interview DTO to `IInterview`. Tolerates `interviewers` being
   * absent, and `roundNumber` defaulting to 1. Keep this aligned with the backend
   * `InterviewDto`; a field rename is a one-line change here.
   */
  private mapInterview(r: Partial<IInterview> | null | undefined): IInterview {
    const src = r ?? {};
    return {
      id: src.id ?? '',
      applicantId: src.applicantId ?? '',
      vacancyId: src.vacancyId ?? '',
      roundNumber: src.roundNumber ?? 1,
      interviewType: src.interviewType ?? 'in-person',
      scheduledDate: src.scheduledDate ?? '',
      startTime: src.startTime ?? '',
      durationMinutes: src.durationMinutes ?? 0,
      location: src.location ?? null,
      videoLink: src.videoLink ?? null,
      notes: src.notes ?? null,
      status: src.status ?? 'Scheduled',
      // Backend InterviewerDto uses `fullName`; the UI model uses `name`. Tolerate both.
      interviewers: (src.interviewers ?? []).map((i) => {
        const x = i as { employeeId?: string; name?: string; fullName?: string };
        return { employeeId: x.employeeId ?? '', name: x.name ?? x.fullName ?? '' };
      }),
      applicantName: src.applicantName ?? null,
      vacancyTitle: src.vacancyTitle ?? null,
      createdAt: src.createdAt ?? null,
    };
  }

  /**
   * Normalize a schedule/reschedule response. The backend may return either
   * `{ interview, warnings }` (soft-conflict aware, FR-7) or a bare interview DTO;
   * both collapse to `IScheduleResult` with `warnings` defaulting to `[]`.
   */
  private mapScheduleResult(res: unknown): IScheduleResult {
    const obj = (res ?? {}) as {
      interview?: Partial<IInterview>;
      warnings?: string[] | null;
    } & Partial<IInterview>;
    const interviewSrc = obj.interview ?? (obj as Partial<IInterview>);
    return {
      interview: this.mapInterview(interviewSrc),
      warnings: obj.warnings ?? [],
    };
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
