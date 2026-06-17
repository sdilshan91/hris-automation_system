import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpRequest,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IAsset,
  IAssignedAsset,
  IIssueAssetsRequest,
} from '../models/onboarding-asset.models';

/**
 * US-ONB-004: Asset issuance + register service.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (NFR-2).
 * Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope, so this service never touches `.data` (matches
 * OnboardingChecklistService and every other FE feature service in this repo).
 *
 * PINNED CONTRACT (backend builds these EXACTLY — do not drift):
 *   GET  /onboarding/assets/available?assetType=:type -> IAsset[]
 *   GET  /onboarding/assets?employeeId=:id            -> IAssignedAsset[]  (HR view)
 *   GET  /onboarding/assets/me                         -> IAssignedAsset[]  (self-service, read-only)
 *   POST /onboarding/assets/issue                      -> multipart:
 *        JSON field "request" = IIssueAssetsRequest, optional file field "acknowledgment"
 *        -> IAssignedAsset[]  (issued records)
 */
@Injectable({ providedIn: 'root' })
export class OnboardingAssetService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/onboarding/assets`;

  // ─── Read ─────────────────────────────────────────────────

  /**
   * Available (status === 'available') assets in the register, optionally
   * filtered by type for the type-ahead dropdown (AC-1 / FR-3). Backend returns
   * only assets eligible for issuance.
   */
  getAvailableAssets(assetType?: string | null): Observable<IAsset[]> {
    const params: Record<string, string> = {};
    if (assetType) params['assetType'] = assetType;
    return this.http.get<IAsset[]>(`${this.base}/available`, {
      params,
      withCredentials: true,
    });
  }

  /** Assets currently assigned to a specific employee — HR view (AC-4 source). */
  getEmployeeAssets(employeeId: string): Observable<IAssignedAsset[]> {
    return this.http.get<IAssignedAsset[]>(this.base, {
      params: { employeeId },
      withCredentials: true,
    });
  }

  /**
   * The logged-in employee's own assigned assets — read-only self-service view
   * (AC-4 / BR-6). Identity + tenant are server-resolved from the session; no
   * employeeId is sent.
   */
  getMyAssets(): Observable<IAssignedAsset[]> {
    return this.http.get<IAssignedAsset[]>(`${this.base}/me`, {
      withCredentials: true,
    });
  }

  // ─── Write (tenant_id is server-set — FR-7) ───────────────

  /**
   * Issue one or more assets to an employee in a single atomic submission
   * (FR-5 / NFR-5). The body is multipart: the JSON request is sent as a "request"
   * field and the optional signed-receipt as "acknowledgment" (FR-6). The
   * progress stream is exposed so the upload bar can render. Double-assignment
   * (AC-3, BR-1) and unavailable-asset (FR-3) errors come back as a 4xx whose
   * message the caller surfaces verbatim.
   */
  issueAssets(
    request: IIssueAssetsRequest,
    acknowledgmentFile?: File | null,
  ): Observable<HttpEvent<IAssignedAsset[]>> {
    const formData = new FormData();
    formData.append('request', JSON.stringify(request));
    if (acknowledgmentFile) {
      formData.append(
        'acknowledgment',
        acknowledgmentFile,
        acknowledgmentFile.name,
      );
    }
    const req = new HttpRequest<FormData>(
      'POST',
      `${this.base}/issue`,
      formData,
      { reportProgress: true, withCredentials: true },
    );
    return this.http.request<IAssignedAsset[]>(req);
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
}
