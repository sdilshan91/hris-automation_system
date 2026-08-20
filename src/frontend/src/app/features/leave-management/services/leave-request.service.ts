import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILeaveRequest,
  ICreateLeaveRequest,
  ILeaveBalance,
  ILeaveRequestErrorResponse,
  ICancelLeaveRequest,
  ICancelLeaveErrorResponse,
  ILeaveCancellationResult,
  LeaveRequestWire,
  mapLeaveRequest,
  LeaveCancellationResultWire,
  mapLeaveCancellationResult,
} from '../models/leave-request.models';

/**
 * US-LV-003: Service for applying for leave + listing the employee's own requests.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   POST /api/v1/leaves            - create a leave request (FR-5); returns ILeaveRequest
 *   GET  /api/v1/leaves/mine       - current employee's own leave requests (My Leaves list)
 *   GET  /api/v1/leaves/my-balance - current employee's leave balances per type (FR-2, AC-2)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the leaves resource is `${apiBaseUrl}/leaves`.
 */
/**
 * Raw balance summary as returned by `GET /api/v1/leaves/my-balance`
 * (backend `LeaveBalanceDto`, US-LV-006). Only the fields the apply form's inline
 * preview consumes are typed here; mapped to `ILeaveBalance` in `getMyBalances`.
 */
interface IMyBalanceResponse {
  leaveTypeId: string;
  entitlement: number;
  used: number;
  balance: number;
}

@Injectable({ providedIn: 'root' })
export class LeaveRequestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  // --- Write -------------------------------------------------

  /** Submit a new leave request (FR-5, AC-1). Returns the created request. */
  createLeaveRequest(request: ICreateLeaveRequest): Observable<ILeaveRequest> {
    return this.http
      .post<LeaveRequestWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapLeaveRequest));
  }

  /**
   * US-LV-010: Cancel one of the employee's own leave requests (FR-1, AC-1/AC-2).
   *
   *   POST /api/v1/leaves/{id}/cancel  body { reason }  -> LeaveCancellationResultDto (status 'Cancelled')
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
  ): Observable<ILeaveCancellationResult> {
    return this.http
      .post<LeaveCancellationResultWire>(
        `${this.baseUrl}/${requestId}/cancel`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapLeaveCancellationResult));
  }

  // --- Read --------------------------------------------------

  /** Get the current employee's own leave requests (My Leaves list). */
  getMyLeaveRequests(): Observable<ILeaveRequest[]> {
    return this.http
      .get<LeaveRequestWire[]>(`${this.baseUrl}/mine`, { withCredentials: true })
      .pipe(map((rows) => (rows ?? []).map(mapLeaveRequest)));
  }

  /**
   * Get the current employee's leave balances per leave type (FR-2, AC-2).
   * Used for the real-time inline balance preview on the apply form.
   *
   * Backend route is `GET /api/v1/leaves/my-balance` (LeaveRequestsController), returning
   * `LeaveBalanceDto[]` (US-LV-006). Its field names differ from the FE `ILeaveBalance`
   * projection, so we map: entitlement -> entitlementDays, used -> usedDays,
   * balance -> remainingDays. (The ApiResponse envelope is unwrapped globally by
   * apiEnvelopeInterceptor before it reaches here.)
   */
  getMyBalances(): Observable<ILeaveBalance[]> {
    return this.http
      .get<IMyBalanceResponse[]>(`${this.baseUrl}/my-balance`, {
        withCredentials: true,
      })
      .pipe(
        map((rows) =>
          rows.map((r) => ({
            leaveTypeId: r.leaveTypeId,
            entitlementDays: r.entitlement,
            usedDays: r.used,
            remainingDays: r.balance,
          })),
        ),
      );
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
