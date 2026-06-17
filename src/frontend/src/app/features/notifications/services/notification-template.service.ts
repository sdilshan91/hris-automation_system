import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IRenderedPreview,
  ITemplateDetail,
  ITemplateListItem,
  ITemplatePreviewRequest,
  ITemplateUpdate,
  ITestEmailRequest,
  ITestEmailResult,
} from '../models/notification-template.models';

/**
 * US-NTF-002: Email Notification Templates per Tenant.
 *
 * Tenant-scoped service for the Tenant Admin template editor. Endpoints live
 * under `/api/v1/notification-templates...` — base derived from
 * `environment.apiBaseUrl` verbatim (which already ends in `/v1`), matching how
 * the sibling US-NTF-001 NotificationService + US-ADM-006 company-settings built
 * their bases. Tenant isolation is enforced server-side (RLS + EF global filters,
 * AC-5); the FE just carries the auth cookie + the X-Tenant-Subdomain header
 * (added by the tenantInterceptor) on every call.
 *
 * Responses are the BARE payload — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the ApiResponse<T> envelope before it reaches here.
 *
 * Holds two signals so the list + editor screens are reactive:
 *  - `templates()` — the event-type list (AC-1);
 *  - `currentTemplate()` — the template open in the editor (AC-2).
 */
@Injectable({ providedIn: 'root' })
export class NotificationTemplateService {
  private readonly http = inject(HttpClient);

  /** `${apiBaseUrl}/notification-templates` (apiBaseUrl already ends with /api/v1). */
  private readonly baseUrl = `${environment.apiBaseUrl}/notification-templates`;

  // ─── Reactive state ───────────────────────────────────────
  readonly templates = signal<ITemplateListItem[]>([]);
  readonly currentTemplate = signal<ITemplateDetail | null>(null);
  readonly loadingList = signal(false);
  readonly loadingTemplate = signal(false);
  readonly saving = signal(false);

  /** Count of customised (override) event types — small dashboard hint (AC-1). */
  readonly customCount = computed(
    () => this.templates().filter((t) => t.isCustom).length,
  );

  // ─── List (AC-1) ──────────────────────────────────────────

  /** Load all event-type rows for the given language (default "en"). */
  loadTemplates(language = 'en'): void {
    this.loadingList.set(true);
    const params = new HttpParams().set('language', language);
    this.http
      .get<ITemplateListItem[]>(this.baseUrl, { params, withCredentials: true })
      .subscribe({
        next: (rows) => {
          this.templates.set(rows);
          this.loadingList.set(false);
        },
        error: () => this.loadingList.set(false),
      });
  }

  // ─── Detail (AC-2) ────────────────────────────────────────

  /** Load one template (effective: tenant override or system default, FR-6). */
  loadTemplate(eventKey: string, language = 'en'): void {
    this.loadingTemplate.set(true);
    this.currentTemplate.set(null);
    const params = new HttpParams().set('language', language);
    this.http
      .get<ITemplateDetail>(`${this.baseUrl}/${eventKey}`, {
        params,
        withCredentials: true,
      })
      .subscribe({
        next: (detail) => {
          this.currentTemplate.set(detail);
          this.loadingTemplate.set(false);
        },
        error: () => this.loadingTemplate.set(false),
      });
  }

  // ─── Save override (AC-3) ─────────────────────────────────

  /**
   * Persist the editable fields as a tenant override. The backend stamps the
   * tenant_id from the session (FR-10) and returns the saved detail (version
   * bumped, isCustom=true). We update `currentTemplate` on success.
   */
  saveTemplate(
    eventKey: string,
    body: ITemplateUpdate,
    language = 'en',
  ): Observable<ITemplateDetail> {
    this.saving.set(true);
    const params = new HttpParams().set('language', language);
    return this.http
      .put<ITemplateDetail>(`${this.baseUrl}/${eventKey}`, body, {
        params,
        withCredentials: true,
      })
      .pipe(
        tap({
          next: (detail) => {
            this.currentTemplate.set(detail);
            this.markRowCustom(eventKey, detail);
            this.saving.set(false);
          },
          error: () => this.saving.set(false),
        }),
      );
  }

  // ─── Reset to default (AC-4) ──────────────────────────────

  /**
   * Remove the tenant override (soft-delete server-side) and return the effective
   * system default so the editor can re-populate. The list row flips to "Default".
   */
  resetToDefault(
    eventKey: string,
    language = 'en',
  ): Observable<ITemplateDetail> {
    this.saving.set(true);
    const params = new HttpParams().set('language', language);
    return this.http
      .delete<ITemplateDetail>(`${this.baseUrl}/${eventKey}`, {
        params,
        withCredentials: true,
      })
      .pipe(
        tap({
          next: (detail) => {
            this.currentTemplate.set(detail);
            this.markRowCustom(eventKey, detail);
            this.saving.set(false);
          },
          error: () => this.saving.set(false),
        }),
      );
  }

  // ─── Live preview (FR-4) ──────────────────────────────────

  /** Render the in-progress draft against the template's sample data. */
  preview(
    eventKey: string,
    body: ITemplatePreviewRequest,
  ): Observable<IRenderedPreview> {
    return this.http.post<IRenderedPreview>(
      `${this.baseUrl}/${eventKey}/preview`,
      body,
      { withCredentials: true },
    );
  }

  // ─── Test email (FR-8) ────────────────────────────────────

  /** Send a one-off rendered test email to the supplied recipient. */
  sendTestEmail(
    eventKey: string,
    body: ITestEmailRequest,
  ): Observable<ITestEmailResult> {
    return this.http.post<ITestEmailResult>(
      `${this.baseUrl}/${eventKey}/test-email`,
      body,
      { withCredentials: true },
    );
  }

  // ─── Helpers ──────────────────────────────────────────────

  /** Keep the cached list row in sync after a save/reset, without a re-fetch. */
  private markRowCustom(eventKey: string, detail: ITemplateDetail): void {
    this.templates.update((rows) =>
      rows.map((r) =>
        r.eventKey === eventKey && r.language === detail.language
          ? { ...r, isCustom: detail.isCustom, lastModified: new Date().toISOString() }
          : r,
      ),
    );
  }
}
