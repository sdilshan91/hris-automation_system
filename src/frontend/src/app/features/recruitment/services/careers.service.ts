import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
  HttpParams,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { filter, map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IApplicant,
  IApplicantErrorResponse,
  IApplicationRequest,
  IPublicVacancy,
  IPublicVacancyListParams,
  RESUME_ALLOWED_EXTENSIONS,
  RESUME_ALLOWED_MIME,
  RESUME_MAX_BYTES,
  DUPLICATE_APPLICATION_CODE,
} from '../models/applicant.models';

/**
 * US-REC-002: Service for the public careers experience + application submission.
 *
 * PUBLIC reads (`/careers/vacancies`) are anonymous (no `withCredentials`) so the
 * page works without a session. The INTERNAL apply path (AC-4 / FR-8) uses
 * `withCredentials` for the authenticated employee's httpOnly cookie. All requests
 * are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Responses are the BARE `T` — the global apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the backend `ApiResponse<T>` envelope, so this service never touches
 * `.data` itself.
 *
 * Resume uploads go as multipart/form-data with `reportProgress` so the form can
 * render an upload progress bar (§8 / NFR-1). The emitted stream yields progress
 * percentages and finally the parsed `IApplicant` — see `IUploadEvent`.
 */

/** Progress/result events surfaced to the application form during upload. */
export type IUploadEvent =
  | { type: 'progress'; progress: number }
  | { type: 'done'; applicant: IApplicant };

@Injectable({ providedIn: 'root' })
export class CareersService {
  private readonly http = inject(HttpClient);
  private readonly careersBase = `${environment.apiBaseUrl}/careers/vacancies`;
  private readonly recruitmentBase = `${environment.apiBaseUrl}/recruitment/vacancies`;

  // ─── Public reads (anonymous) ────────────────────────────

  /** Open vacancies for the public careers page (§8). Anonymous — no credentials. */
  listOpenVacancies(
    params: IPublicVacancyListParams = {},
  ): Observable<IPublicVacancy[]> {
    let httpParams = new HttpParams();
    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    if (params.departmentName) {
      httpParams = httpParams.set('departmentName', params.departmentName);
    }
    if (params.locationName) {
      httpParams = httpParams.set('locationName', params.locationName);
    }
    if (params.employmentType) {
      httpParams = httpParams.set('employmentType', params.employmentType);
    }
    return this.http
      .get<IPublicVacancy[] | { data: IPublicVacancy[] }>(this.careersBase, {
        params: httpParams,
      })
      .pipe(map((res) => this.normalizeList(res)));
  }

  /**
   * Single public vacancy detail by SLUG (§8). Anonymous — no credentials.
   *
   * GAP-011: this took an `id` and interpolated it into `GET /careers/vacancies/{slug}`, so every detail
   * fetch 404'd. The route is keyed by slug; the vacancy ID is what the APPLY route needs, and both are now
   * on the public DTOs.
   */
  getPublicVacancy(slug: string): Observable<IPublicVacancy> {
    return this.http.get<IPublicVacancy>(`${this.careersBase}/${slug}`);
  }

  // ─── Apply (multipart, progress) ─────────────────────────

  /**
   * Submit a PUBLIC application (AC-1). Anonymous multipart POST that streams
   * upload progress and finally the created applicant. Duplicate (AC-3) and file
   * rejection (AC-2) surface on the error channel for the caller to map.
   */
  applyPublic(
    vacancyId: string,
    request: IApplicationRequest,
    resume: File,
  ): Observable<IUploadEvent> {
    const form = CareersService.buildFormData(request, resume);
    return this.uploadApplication(
      `${this.careersBase}/${vacancyId}/apply`,
      form,
      false,
    );
  }

  /**
   * Submit an INTERNAL application as an authenticated employee (AC-4 / FR-8).
   * Sends credentials so the backend can link the application to the employee
   * record; the same multipart shape + progress stream as the public path.
   */
  applyInternal(
    vacancyId: string,
    request: IApplicationRequest,
    resume: File,
  ): Observable<IUploadEvent> {
    const form = CareersService.buildFormData(request, resume);
    return this.uploadApplication(
      `${this.recruitmentBase}/${vacancyId}/applicants`,
      form,
      true,
    );
  }

  private uploadApplication(
    url: string,
    form: FormData,
    withCredentials: boolean,
  ): Observable<IUploadEvent> {
    return this.http
      .post<IApplicant>(url, form, {
        reportProgress: true,
        observe: 'events',
        withCredentials,
      })
      .pipe(
        filter(
          (e: HttpEvent<IApplicant>) =>
            e.type === HttpEventType.UploadProgress ||
            e.type === HttpEventType.Response,
        ),
        map((e: HttpEvent<IApplicant>): IUploadEvent => {
          if (e.type === HttpEventType.UploadProgress) {
            const progress = e.total
              ? Math.round((100 * e.loaded) / e.total)
              : 0;
            return { type: 'progress', progress };
          }
          // HttpEventType.Response — body is the bare IApplicant (envelope unwrapped).
          return { type: 'done', applicant: (e as { body: IApplicant }).body };
        }),
      );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Assemble the multipart payload. Field naming kept in ONE place for a 1-line fix. */
  static buildFormData(request: IApplicationRequest, resume: File): FormData {
    const form = new FormData();
    form.append('firstName', request.firstName);
    form.append('lastName', request.lastName);
    form.append('email', request.email);
    if (request.phone) {
      form.append('phone', request.phone);
    }
    if (request.coverLetter) {
      form.append('coverLetter', request.coverLetter);
    }
    form.append('resume', resume, resume.name);
    return form;
  }

  /** Tolerate a bare array OR a `{ data }` envelope from the public listing. */
  private normalizeList(
    res: IPublicVacancy[] | { data: IPublicVacancy[] },
  ): IPublicVacancy[] {
    if (Array.isArray(res)) {
      return res;
    }
    return res?.data ?? [];
  }

  // ─── Client-side file validation (AC-2 / BR-4) ───────────

  /**
   * Validate a candidate resume file client-side BEFORE upload (AC-2). Returns an
   * i18n-free message key/null. Extension is the authoritative check (browsers
   * report `.doc` MIME inconsistently); MIME is a secondary signal when present.
   * Returns null when the file is acceptable.
   */
  static validateResume(file: File): 'too_large' | 'bad_type' | null {
    if (file.size > RESUME_MAX_BYTES) {
      return 'too_large';
    }
    const name = file.name.toLowerCase();
    const extOk = RESUME_ALLOWED_EXTENSIONS.some((ext) => name.endsWith(ext));
    if (!extOk) {
      return 'bad_type';
    }
    // If the browser supplied a MIME type at all, it must be in the allow-list.
    if (file.type && !RESUME_ALLOWED_MIME.includes(file.type)) {
      return 'bad_type';
    }
    return null;
  }

  // ─── Error parsing ───────────────────────────────────────

  /** Parse an error body into the typed shape; the caller shows `message` verbatim. */
  static parseError(err: HttpErrorResponse): IApplicantErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IApplicantErrorResponse;
    }
    return null;
  }

  /** Convenience: human-readable message from an error response. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return (
      CareersService.parseError(err)?.message ??
      'An unexpected error occurred. Please try again.'
    );
  }

  /** True when the backend rejected the application as a duplicate (AC-3). */
  static isDuplicate(err: HttpErrorResponse): boolean {
    if (err.status === 409) {
      return true;
    }
    return CareersService.parseError(err)?.code === DUPLICATE_APPLICATION_CODE;
  }
}
