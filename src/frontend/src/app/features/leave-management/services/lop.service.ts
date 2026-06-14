import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILopEntry,
  IAssignLopRequest,
  IAssignLopResult,
  IAssignCompulsoryLeaveRequest,
  IAssignCompulsoryLeaveResult,
  IOverrideLopRequest,
  ILopErrorResponse,
} from '../models/lop.models';
import { ILeaveRequest } from '../models/leave-request.models';

/**
 * US-LV-011: Service for Loss-of-Pay (LOP) / compulsory-leave HR management.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). LOP data
 * is tenant-isolated server-side (NFR-2).
 *
 * `environment.apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves`.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel; see
 * the vault "Frontend (US-LV-011)" section for reconciliation):
 *   GET  /api/v1/leaves/lop-summary?employeeId&from&to   - list LOP entries (FR-5)
 *   POST /api/v1/leaves/assign-lop                        - bulk LOP assign (FR-3)
 *   POST /api/v1/leaves/compulsory                        - compulsory leave (FR-6)
 *   POST /api/v1/leaves/lop/{id}/override                 - override system LOP (BR-3)
 */
@Injectable({ providedIn: 'root' })
export class LopService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  // --- Read --------------------------------------------------

  /**
   * List LOP entries for display (FR-5).
   *
   * `employeeId` scopes to one employee; omit it for the whole tenant. `from`/`to`
   * bound the period (date-only 'YYYY-MM-DD'). The source/status filters are applied
   * client-side over the returned rows (the filter chips), so all sources are fetched.
   */
  getLopSummary(params?: {
    employeeId?: string | null;
    from?: string | null;
    to?: string | null;
  }): Observable<ILopEntry[]> {
    let httpParams = new HttpParams();
    if (params?.employeeId) {
      httpParams = httpParams.set('employeeId', params.employeeId);
    }
    if (params?.from) {
      httpParams = httpParams.set('from', params.from);
    }
    if (params?.to) {
      httpParams = httpParams.set('to', params.to);
    }
    return this.http.get<ILopEntry[]>(`${this.baseUrl}/lop-summary`, {
      params: httpParams,
      withCredentials: true,
    });
  }

  // --- Write -------------------------------------------------

  /** Bulk-assign LOP days to one employee (FR-3, AC-3). */
  assignLop(request: IAssignLopRequest): Observable<IAssignLopResult> {
    return this.http.post<IAssignLopResult>(`${this.baseUrl}/assign-lop`, request, {
      withCredentials: true,
    });
  }

  /**
   * Assign compulsory leave (company shutdown) to all / selected employees (FR-6).
   * The backend deducts from balance first, falling back to LOP (BR-4).
   */
  assignCompulsoryLeave(
    request: IAssignCompulsoryLeaveRequest,
  ): Observable<IAssignCompulsoryLeaveResult> {
    return this.http.post<IAssignCompulsoryLeaveResult>(
      `${this.baseUrl}/compulsory`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Override a system-generated LOP entry by converting it to a different leave
   * type (BR-3). Returns the updated leave request.
   */
  overrideLop(
    leaveRequestId: string,
    request: IOverrideLopRequest,
  ): Observable<ILeaveRequest> {
    return this.http.post<ILeaveRequest>(
      `${this.baseUrl}/lop/${leaveRequestId}/override`,
      request,
      { withCredentials: true },
    );
  }

  // --- Error helper ------------------------------------------

  /** Parse an error response into a typed LOP error. */
  static parseError(err: HttpErrorResponse): ILopErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ILopErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from an error. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return LopService.parseError(err)?.message ?? 'An unexpected error occurred.';
  }
}
