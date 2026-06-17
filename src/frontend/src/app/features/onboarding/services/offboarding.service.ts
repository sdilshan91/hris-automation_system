import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IOffboardingInstance,
  IInitiateOffboardingRequest,
  IRecordClearanceRequest,
  IReturnAssetRequest,
  ICompleteBlockedError,
} from '../models/offboarding.models';

/**
 * US-ONB-005: Offboarding / exit-checklist + clearance service.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-6 / NFR-2).
 * Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope, so this service never touches `.data` (matches
 * OnboardingAssetService and every other FE feature service in this repo).
 *
 * PINNED CONTRACT (backend builds these EXACTLY — do not drift):
 *   POST /offboarding/initiate                       -> IOffboardingInstance
 *   GET  /offboarding/{id}                            -> IOffboardingInstance
 *   GET  /offboarding?employeeId={id}                 -> IOffboardingInstance (or 404)
 *   POST /offboarding/tasks/{taskId}/clearance        -> { status, remarks? }
 *   POST /offboarding/tasks/{taskId}/return-asset     -> { assetId, condition, disposed? }
 *   POST /offboarding/{id}/complete                   -> 409 { pending: [titles] } if blocked
 */
@Injectable({ providedIn: 'root' })
export class OffboardingService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/offboarding`;

  // ─── Initiate (AC-1 / FR-2 / FR-8) ─────────────────────────

  /**
   * Generate an exit checklist from the tenant template for a departing employee.
   * `tenant_id` is server-set from the session (FR-8). A BR-1 status violation or
   * an "already offboarding" conflict comes back as a 4xx the caller surfaces.
   */
  initiate(
    request: IInitiateOffboardingRequest,
  ): Observable<IOffboardingInstance> {
    return this.http.post<IOffboardingInstance>(`${this.base}/initiate`, request, {
      withCredentials: true,
    });
  }

  // ─── Read (AC-3 dashboard source) ──────────────────────────

  /** The full offboarding instance with department lanes + tasks. */
  getById(offboardingId: string): Observable<IOffboardingInstance> {
    return this.http.get<IOffboardingInstance>(`${this.base}/${offboardingId}`, {
      withCredentials: true,
    });
  }

  /**
   * The employee's offboarding instance, if one exists (404 otherwise). Used to
   * jump from the initiate form straight to an existing dashboard.
   */
  getByEmployee(employeeId: string): Observable<IOffboardingInstance> {
    return this.http.get<IOffboardingInstance>(this.base, {
      params: { employeeId },
      withCredentials: true,
    });
  }

  // ─── Clearance + asset return (AC-2 / AC-3 / FR-9) ─────────

  /**
   * A department head records a per-task clearance decision: 'approved' or
   * 'pending_issues' with optional remarks (AC-3). The server recomputes the
   * department + overall clearance and writes an audit record (FR-9), then returns
   * the refreshed instance.
   */
  recordClearance(
    taskId: string,
    request: IRecordClearanceRequest,
  ): Observable<IOffboardingInstance> {
    return this.http.post<IOffboardingInstance>(
      `${this.base}/tasks/${taskId}/clearance`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Mark a register-tracked asset as returned during offboarding (AC-2). The
   * server flips the asset to 'available' (or 'disposed'), completes the task,
   * and writes an audit record (BR-3 / FR-9), returning the refreshed instance.
   */
  returnAsset(
    taskId: string,
    request: IReturnAssetRequest,
  ): Observable<IOffboardingInstance> {
    return this.http.post<IOffboardingInstance>(
      `${this.base}/tasks/${taskId}/return-asset`,
      request,
      { withCredentials: true },
    );
  }

  // ─── Complete (AC-4 / AC-5 / FR-5..FR-7) ───────────────────

  /**
   * Finalize offboarding: deactivate the account, trigger the F&F notification,
   * revoke sessions (FR-5..FR-7). Blocked with 409 + { pending: [titles] } when
   * mandatory items remain (AC-5); the caller surfaces that list inline.
   */
  complete(offboardingId: string): Observable<IOffboardingInstance> {
    return this.http.post<IOffboardingInstance>(
      `${this.base}/${offboardingId}/complete`,
      {},
      { withCredentials: true },
    );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Parse an error body into a human-readable message; caller shows verbatim. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred.';
  }

  /**
   * Extract the pending-mandatory titles from a 409 complete-blocked response
   * (AC-5). Returns null when the error is not the structured block, so the
   * caller can fall back to a generic message.
   */
  static parseCompleteBlocked(err: HttpErrorResponse): string[] | null {
    if (err.status !== 409) return null;
    const body = err.error as Partial<ICompleteBlockedError> | undefined;
    if (body && Array.isArray(body.pending)) return body.pending;
    return null;
  }
}
