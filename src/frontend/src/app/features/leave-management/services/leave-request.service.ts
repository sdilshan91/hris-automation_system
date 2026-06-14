import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILeaveRequest,
  ICreateLeaveRequest,
  ILeaveBalance,
  ILeaveRequestErrorResponse,
  ICancelLeaveRequest,
  ICancelLeaveErrorResponse,
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

  /**
   * US-LV-010: Cancel one of the employee's own leave requests (FR-1, AC-1/AC-2).
   *
   *   POST /api/v1/leaves/{id}/cancel  body { reason }  -> ILeaveRequest (status 'Cancelled')
   *
   * `reason` is required for approved requests (BR-5) and may be empty for pending.
   * Errors the caller maps to §8 UX:
   *   - 400 -> ineligible (already started AC-3, payroll-locked AC-4); show `message` verbatim.
   *   - 409 -> concurrency conflict (manager actioned it first); toast `message` + refresh.
   * The backend remains the source of truth for eligibility; the FE only pre-blocks on
   * the status/date signals it can see (see `evaluateCancelEligibility`).
   */
  cancelLeaveRequest(
    requestId: string,
    body: ICancelLeaveRequest,
  ): Observable<ILeaveRequest> {
    return this.http.post<ILeaveRequest>(
      `${this.baseUrl}/${requestId}/cancel`,
      body,
      { withCredentials: true },
    );
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

  /** US-LV-010: Parse a cancel error body into the typed shape (AC-3, AC-4, concurrency). */
  static parseCancelError(err: HttpErrorResponse): ICancelLeaveErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ICancelLeaveErrorResponse;
    }
    return null;
  }
}
