import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IOnboardingLookups,
  IOnboardingTemplate,
  IOnboardingTemplateSummary,
  ICreateOnboardingTemplateRequest,
} from '../models/onboarding-template.models';

/**
 * US-ONB-001: Service for onboarding checklist templates.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 /
 * NFR-2). Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope, so this service never touches `.data` (matches every
 * other FE feature service in this repo).
 *
 * CONTRACT (assumed — reconcile with backend; mapping kept in ONE place here):
 *   GET    /onboarding/templates                 -> IOnboardingTemplateSummary[]
 *   GET    /onboarding/templates/:id             -> IOnboardingTemplate
 *   POST   /onboarding/templates                 body ICreate... -> IOnboardingTemplate
 *   POST   /onboarding/templates/:id/clone       -> IOnboardingTemplate (pre-filled copy)
 *   PATCH  /onboarding/templates/:id/activate    -> IOnboardingTemplateSummary
 *   PATCH  /onboarding/templates/:id/deactivate  -> IOnboardingTemplateSummary
 *   GET    /onboarding/templates/lookups         -> IOnboardingLookups (depts/titles/users)
 */
@Injectable({ providedIn: 'root' })
export class OnboardingTemplateService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/onboarding/templates`;

  // ─── Read (FR-6 / FR-7 list view) ────────────────────────

  /**
   * All templates for the current tenant (active + inactive, BR-4).
   *
   * The backend now returns a paginated envelope `{ items, totalCount, ... }`
   * (previously `{ data, total }`); tolerate a bare array, the new `items`, and
   * the legacy `data` so the list never goes silently empty across the rollout.
   */
  list(): Observable<IOnboardingTemplateSummary[]> {
    return this.http
      .get<
        | IOnboardingTemplateSummary[]
        | { items?: IOnboardingTemplateSummary[]; data?: IOnboardingTemplateSummary[] }
      >(this.base, { withCredentials: true })
      .pipe(map((res) => this.toArray(res)));
  }

  /** A single template with its full task list (for clone/edit pre-fill). */
  get(id: string): Observable<IOnboardingTemplate> {
    return this.http.get<IOnboardingTemplate>(`${this.base}/${id}`, {
      withCredentials: true,
    });
  }

  /** Scope/responsibility lookups for the builder (depts, job titles, users). */
  getLookups(): Observable<IOnboardingLookups> {
    return this.http.get<IOnboardingLookups>(`${this.base}/lookups`, {
      withCredentials: true,
    });
  }

  // ─── Write (AC-2 / FR-5 — tenant_id is server-set) ───────

  /** Create a new template with its tasks. */
  create(
    request: ICreateOnboardingTemplateRequest,
  ): Observable<IOnboardingTemplate> {
    return this.http.post<IOnboardingTemplate>(this.base, request, {
      withCredentials: true,
    });
  }

  /**
   * Clone an existing template (FR-6). Backend returns a NEW unsaved/draft copy
   * with tasks duplicated and "(Copy)" appended to the name (UI/UX §8); the
   * builder loads it for review before the user saves.
   */
  clone(id: string): Observable<IOnboardingTemplate> {
    return this.http.post<IOnboardingTemplate>(
      `${this.base}/${id}/clone`,
      {},
      { withCredentials: true },
    );
  }

  /** Activate a template (FR-7 soft status toggle). The endpoint is a POST. */
  activate(id: string): Observable<IOnboardingTemplateSummary> {
    return this.http.post<IOnboardingTemplateSummary>(
      `${this.base}/${id}/activate`,
      {},
      { withCredentials: true },
    );
  }

  /** Deactivate a template (FR-7 / BR-4 — stays visible, not assignable). POST endpoint. */
  deactivate(id: string): Observable<IOnboardingTemplateSummary> {
    return this.http.post<IOnboardingTemplateSummary>(
      `${this.base}/${id}/deactivate`,
      {},
      { withCredentials: true },
    );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /**
   * Accept a bare array, the new `{ items }` page envelope, or the legacy
   * `{ data }` one; default to []. Items first, then data, then [].
   */
  private toArray<T>(
    res: T[] | { items?: T[]; data?: T[] } | null | undefined,
  ): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { items?: T[] }).items)) {
      return (res as { items: T[] }).items;
    }
    if (res && Array.isArray((res as { data?: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
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
