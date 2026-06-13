import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILeaveRequest,
  ICreateLeaveRequest,
  ILeaveBalance,
  ILeaveRequestErrorResponse,
} from '../models/leave-request.models';

/**
 * US-LV-003: Service for applying for leave + listing the employee's own requests.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   POST /api/v1/leaves          - create a leave request (FR-5); returns ILeaveRequest
 *   GET  /api/v1/leaves/mine     - current employee's own leave requests (My Leaves list)
 *   GET  /api/v1/leaves/balances - current employee's leave balances per type (FR-2, AC-2)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the leaves resource is `${apiBaseUrl}/leaves`.
 */
@Injectable({ providedIn: 'root' })
export class LeaveRequestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  // --- Write -------------------------------------------------

  /** Submit a new leave request (FR-5, AC-1). Returns the created request. */
  createLeaveRequest(request: ICreateLeaveRequest): Observable<ILeaveRequest> {
    return this.http.post<ILeaveRequest>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  // --- Read --------------------------------------------------

  /** Get the current employee's own leave requests (My Leaves list). */
  getMyLeaveRequests(): Observable<ILeaveRequest[]> {
    return this.http.get<ILeaveRequest[]>(`${this.baseUrl}/mine`, {
      withCredentials: true,
    });
  }

  /**
   * Get the current employee's leave balances per leave type (FR-2, AC-2).
   * Used for the real-time inline balance preview on the apply form.
   */
  getMyBalances(): Observable<ILeaveBalance[]> {
    return this.http.get<ILeaveBalance[]>(`${this.baseUrl}/balances`, {
      withCredentials: true,
    });
  }

  // --- Error helper ------------------------------------------

  /** Parse an error response into a typed leave request error. */
  static parseError(err: HttpErrorResponse): ILeaveRequestErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ILeaveRequestErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from an error. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return LeaveRequestService.parseError(err)?.message ?? 'An unexpected error occurred.';
  }
}
