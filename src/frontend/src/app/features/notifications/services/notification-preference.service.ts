import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ICategoryPreference,
  ICategoryUpdate,
  IPreferenceMatrix,
  IQuietHours,
  IQuietHoursUpdate,
} from '../models/notification-preference.models';

/**
 * US-NTF-003: Notification Preferences per User.
 *
 * Personal, tenant-scoped service backing the Profile > Notification Preferences
 * page. Endpoints live under `/api/v1/notification-preferences...` — the base is
 * `environment.apiBaseUrl` verbatim (already ends in `/api/v1`), matching the
 * sibling US-NTF-001/002 services. Tenant + user isolation is enforced
 * server-side (RLS + EF global filters, BR-4/NFR-2); the FE just carries the auth
 * cookie + the X-Tenant-Subdomain header (added by the tenantInterceptor).
 *
 * Responses are the BARE payload — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope before it reaches here.
 *
 * Reactive state:
 *  - `categories()` — the preference matrix rows (AC-1);
 *  - `quietHours()` — the Quiet Hours setting (FR-9);
 *  - `loading()` / `saving()` — UI flags.
 */
@Injectable({ providedIn: 'root' })
export class NotificationPreferenceService {
  private readonly http = inject(HttpClient);

  /** `${apiBaseUrl}/notification-preferences` (apiBaseUrl already ends with /api/v1). */
  private readonly baseUrl = `${environment.apiBaseUrl}/notification-preferences`;

  // ─── Reactive state ───────────────────────────────────────
  readonly categories = signal<ICategoryPreference[]>([]);
  readonly quietHours = signal<IQuietHours>({
    enabled: false,
    start: null,
    end: null,
    timezone: null,
  });
  readonly loading = signal(false);
  readonly saving = signal(false);

  /** True once a matrix has been loaded — drives empty/skeleton states. */
  readonly hasLoaded = signal(false);

  /** Count of categories the org forces on (small UI hint). */
  readonly mandatoryCount = computed(
    () => this.categories().filter((c) => c.isMandatory).length,
  );

  // ─── Load matrix (AC-1) ───────────────────────────────────

  /** GET the full preference matrix (categories + quiet hours). */
  load(): void {
    this.loading.set(true);
    this.http
      .get<IPreferenceMatrix>(this.baseUrl, { withCredentials: true })
      .subscribe({
        next: (matrix) => {
          this.applyMatrix(matrix);
          this.loading.set(false);
          this.hasLoaded.set(true);
        },
        error: () => this.loading.set(false),
      });
  }

  // ─── Update one category (AC-2/AC-3, BR-3) ────────────────

  /**
   * Persist a category's channel booleans. The backend re-validates (mandatory +
   * "at least one channel" BR-3) and may reject with 400/422 — the caller is
   * responsible for the optimistic toggle + revert-on-error. On success the cached
   * row is replaced with the authoritative server copy.
   */
  updateCategory(
    category: string,
    body: ICategoryUpdate,
  ): Observable<ICategoryPreference> {
    this.saving.set(true);
    return this.http
      .put<ICategoryPreference>(`${this.baseUrl}/${category}`, body, {
        withCredentials: true,
      })
      .pipe(
        tap({
          next: (updated) => {
            this.replaceCategory(updated);
            this.saving.set(false);
          },
          error: () => this.saving.set(false),
        }),
      );
  }

  // ─── Quiet hours (FR-9) ───────────────────────────────────

  /** PUT the Quiet Hours setting; updates the cached signal on success. */
  updateQuietHours(body: IQuietHoursUpdate): Observable<IQuietHours> {
    this.saving.set(true);
    return this.http
      .put<IQuietHours>(`${this.baseUrl}/quiet-hours`, body, {
        withCredentials: true,
      })
      .pipe(
        tap({
          next: (qh) => {
            this.quietHours.set(qh);
            this.saving.set(false);
          },
          error: () => this.saving.set(false),
        }),
      );
  }

  // ─── Reset to defaults (FR-7) ─────────────────────────────

  /** POST reset — restores tenant defaults and returns the fresh matrix. */
  resetToDefaults(): Observable<IPreferenceMatrix> {
    this.saving.set(true);
    return this.http
      .post<IPreferenceMatrix>(`${this.baseUrl}/reset`, {}, { withCredentials: true })
      .pipe(
        tap({
          next: (matrix) => {
            this.applyMatrix(matrix);
            this.saving.set(false);
          },
          error: () => this.saving.set(false),
        }),
      );
  }

  // ─── Helpers ──────────────────────────────────────────────

  private applyMatrix(matrix: IPreferenceMatrix): void {
    this.categories.set(matrix.categories);
    this.quietHours.set(matrix.quietHours);
  }

  /** Swap a single category row in-place after a successful per-category PUT. */
  private replaceCategory(updated: ICategoryPreference): void {
    this.categories.update((rows) =>
      rows.map((r) => (r.category === updated.category ? updated : r)),
    );
  }
}
