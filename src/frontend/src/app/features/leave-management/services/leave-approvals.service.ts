import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IPendingLeaveQuery,
  IPendingLeaveResponse,
  IApiEnvelope,
  IApproveLeaveRequest,
  IRejectLeaveRequest,
  ILeaveActionResult,
  ILeaveActionErrorResponse,
} from '../models/pending-leave.models';

/**
 * US-LV-004: Service for the manager's pending leave-approval queue.
 *
 * Sibling to {@link LeaveRequestService} (US-LV-003): both target the
 * employee/manager-facing `/leaves` resource, but this one is read-only and
 * scoped to the manager's direct reports (BR-1) by the backend.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoint (RECONCILED with the live LeaveRequestsController):
 *   GET /api/v1/leaves/pending
 *     ?leaveTypeId&employeeId&startDate&endDate&sortBy&sortAscending&page&pageSize
 *     -> ApiResponse<PendingLeaveQueueResult>  (envelope: { data: { items, totalCount, ... } })
 *
 * The response is wrapped in the standard `ApiResponse<T>`; this service unwraps `.data`.
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is `${apiBaseUrl}/leaves`.
 *
 * US-LV-005 adds the write actions:
 *   POST /api/v1/leaves/{id}/approve  body { comment?, confirmNegativeBalance? } -> ILeaveActionResult
 *   POST /api/v1/leaves/{id}/reject   body { reason }                            -> ILeaveActionResult
 * Both unwrap the standard ApiResponse<T> envelope (tolerating a bare body too).
 *
 * DEFER: real-time SignalR push (FR-6/AC-5) and multi-level routing (AC-4) are not
 * built here -- the queue exposes a manual refresh and surfaces L2 status as a badge.
 */
@Injectable({ providedIn: 'root' })
export class LeaveApprovalsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  /**
   * Fetch a page of the manager's pending leave queue (FR-1, FR-3, FR-4).
   * All filtering/sorting/paging is round-tripped to the server (AC-2, AC-3).
   */
  getPendingQueue(query: IPendingLeaveQuery): Observable<IPendingLeaveResponse> {
    return this.http
      .get<IApiEnvelope<IPendingLeaveResponse>>(`${this.baseUrl}/pending`, {
        params: this.buildParams(query),
        withCredentials: true,
      })
      .pipe(
        // Unwrap the standard ApiResponse<T> envelope; tolerate a bare body too.
        map((res) => res?.data ?? (res as unknown as IPendingLeaveResponse))
      );
  }

  /** Build HttpParams from the query; omits null/empty optional filters. */
  buildParams(query: IPendingLeaveQuery): HttpParams {
    let params = new HttpParams()
      .set('page', query.page.toString())
      .set('pageSize', query.pageSize.toString());

    if (query.leaveTypeId) {
      params = params.set('leaveTypeId', query.leaveTypeId);
    }
    if (query.employeeId) {
      params = params.set('employeeId', query.employeeId);
    }
    if (query.startDate) {
      params = params.set('startDate', query.startDate);
    }
    if (query.endDate) {
      params = params.set('endDate', query.endDate);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortAscending != null) {
      params = params.set('sortAscending', String(query.sortAscending));
    }

    return params;
  }

  // --- Write actions (US-LV-005) -----------------------------

  /**
   * Approve a pending leave request (AC-1). `comment` is optional (BR-2).
   * Pass `confirmNegativeBalance: true` on the retry after the insufficient-
   * balance confirmation modal (AC-3). Returns the updated request status.
   */
  approve(requestId: string, body: IApproveLeaveRequest = {}): Observable<ILeaveActionResult> {
    return this.http
      .post<IApiEnvelope<ILeaveActionResult>>(
        `${this.baseUrl}/${requestId}/approve`,
        body,
        { withCredentials: true }
      )
      .pipe(map((res) => res?.data ?? (res as unknown as ILeaveActionResult)));
  }

  /**
   * Reject a pending leave request (AC-2). `reason` is mandatory (BR-2) — the
   * component disables submit until it is non-empty, so this is the API guard.
   */
  reject(requestId: string, body: IRejectLeaveRequest): Observable<ILeaveActionResult> {
    return this.http
      .post<IApiEnvelope<ILeaveActionResult>>(
        `${this.baseUrl}/${requestId}/reject`,
        body,
        { withCredentials: true }
      )
      .pipe(map((res) => res?.data ?? (res as unknown as ILeaveActionResult)));
  }

  /** Parse an approve/reject error body into the typed shape (AC-3, AC-5, BR-4). */
  static parseActionError(err: HttpErrorResponse): ILeaveActionErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ILeaveActionErrorResponse;
    }
    return null;
  }
}
