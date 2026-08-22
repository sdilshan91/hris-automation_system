import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IOffboardingInstance,
  IInitiateOffboardingRequest,
  IRecordClearanceRequest,
  IReturnAssetRequest,
  IPendingMandatoryItem,
  OffboardingInstanceWire,
  CompleteOffboardingResultWire,
  mapOffboardingInstance,
  mapPendingMandatoryItem,
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
 *   POST /offboarding/{id}/complete                   -> CompleteOffboardingResultDto; 409 (same DTO
 *                                                        under `data`) when mandatory items block it
 *
 * Every read is typed as the GENERATED `OffboardingInstanceWire` and passed through
 * `mapOffboardingInstance`, so a renamed backend property is a compile error here rather than an
 * `undefined` on the dashboard. The pinned-contract comment above is a *description*; the generated type
 * is the thing that is actually checked.
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
    return this.http
      .post<OffboardingInstanceWire>(`${this.base}/initiate`, request, {
        withCredentials: true,
      })
      .pipe(map(mapOffboardingInstance));
  }

  // ─── Read (AC-3 dashboard source) ──────────────────────────

  /** The full offboarding instance with department lanes + tasks. */
  getById(offboardingId: string): Observable<IOffboardingInstance> {
    return this.http
      .get<OffboardingInstanceWire>(`${this.base}/${offboardingId}`, {
        withCredentials: true,
      })
      .pipe(map(mapOffboardingInstance));
  }

  /**
   * The employee's offboarding instance, if one exists (404 otherwise). Used to
   * jump from the initiate form straight to an existing dashboard.
   */
  getByEmployee(employeeId: string): Observable<IOffboardingInstance> {
    return this.http
      .get<OffboardingInstanceWire>(this.base, {
        params: { employeeId },
        withCredentials: true,
      })
      .pipe(map(mapOffboardingInstance));
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
    return this.http
      .post<OffboardingInstanceWire>(
        `${this.base}/tasks/${taskId}/clearance`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapOffboardingInstance));
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
    return this.http
      .post<OffboardingInstanceWire>(
        `${this.base}/tasks/${taskId}/return-asset`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapOffboardingInstance));
  }

  // ─── Complete (AC-4 / AC-5 / FR-5..FR-7) ───────────────────

  /**
   * Finalize offboarding: deactivate the account, trigger the F&F notification,
   * revoke sessions (FR-5..FR-7).
   *
   * **This endpoint does not return the instance.** It returns
   * `CompleteOffboardingResultDto` — `{ completed, instance, pendingItems, finalSettlementRef }` — and the
   * instance is nested inside it. The previous signature claimed the instance came back directly, so after
   * a successful completion the dashboard re-bound itself to a result object: every field it read was
   * `undefined` and the screen blanked. No spec caught it because they all stub this service, so the real
   * body was never exercised. The generated `CompleteOffboardingResultWire` is what makes the shape checked.
   */
  complete(offboardingId: string): Observable<IOffboardingInstance> {
    return this.http
      .post<CompleteOffboardingResultWire>(
        `${this.base}/${offboardingId}/complete`,
        {},
        { withCredentials: true },
      )
      .pipe(
        map((result) =>
          mapOffboardingInstance(result.instance ?? ({} as OffboardingInstanceWire)),
        ),
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
   * Extract the blocking mandatory items from a 409 complete-blocked response (AC-5). Returns null when the
   * error is not that structured block, so the caller can fall back to a generic message.
   *
   * **The old shape was invented.** This read `err.error.pending` — a flat array of titles that the API has
   * never sent. The real 409 body is the standard failure envelope carrying the result DTO:
   * `{ success: false, code: 'pending_mandatory_items', data: { pendingItems: [...] } }`. Note the error
   * channel is NOT unwrapped by `apiEnvelopeInterceptor` (it only rewrites 2xx bodies), so the envelope is
   * still here and `data` must be stepped through explicitly.
   *
   * Because the invented key never matched, this always returned null and AC-5's whole point — telling the
   * user WHICH items block — degraded to a generic "unexpected error" on every blocked completion.
   */
  static parseCompleteBlocked(err: HttpErrorResponse): IPendingMandatoryItem[] | null {
    if (err.status !== 409) return null;
    const envelope = err.error as
      | { data?: { pendingItems?: unknown } }
      | undefined;
    const items = envelope?.data?.pendingItems;
    if (!Array.isArray(items)) return null;
    return items.map(mapPendingMandatoryItem);
  }
}
