import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ICarryForwardPreviewRow,
  ICarryForwardPreviewError,
} from '../models/carry-forward-preview.models';

/**
 * US-LV-008: Service for the HR-facing carry-forward / expiry preview report.
 *
 * Read-only (§10, AC-5): it only previews what the backend year-end / expiry
 * Hangfire jobs would produce for the selected closing year. All requests are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header) and carry
 * withCredentials for httpOnly cookie auth, matching the established pattern.
 *
 * Backend endpoint (assumed contract — backend agent building in parallel):
 *   GET /api/v1/leaves/carry-forward-preview?year={year}  -> ICarryForwardPreviewRow[]  (FR-5)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves`.
 *
 * No "Run carry-forward now" manual-trigger method is provided: the story's
 * jobs run on a schedule and no manual-trigger endpoint exists in the contract.
 * Per the implementation brief, the button is OMITTED rather than inventing
 * backend behavior. If a trigger endpoint is added later, add a `runNow(year)`
 * method here behind the §10 confirmation dialog.
 */
@Injectable({ providedIn: 'root' })
export class CarryForwardPreviewService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  /**
   * Get the projected carry-forward / forfeiture rows for the given closing year
   * (FR-5, AC-5). An empty array signals the empty state (nothing to process /
   * no eligible balances).
   */
  getPreview(year: number): Observable<ICarryForwardPreviewRow[]> {
    const params = new HttpParams().set('year', String(year));
    return this.http.get<ICarryForwardPreviewRow[]>(`${this.baseUrl}/carry-forward-preview`, {
      params,
      withCredentials: true,
    });
  }

  /** Parse an error response into a typed preview error. */
  static parseError(err: HttpErrorResponse): ICarryForwardPreviewError | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ICarryForwardPreviewError;
    }
    return null;
  }
}
