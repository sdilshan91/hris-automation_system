import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IPendingLeaveQuery,
  IPendingLeaveResponse,
  IApiEnvelope,
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
 * DEFER: Approve/Reject actions are US-LV-005; real-time SignalR push is FR-6/AC-5.
 * Neither is implemented here -- the queue exposes a manual refresh only.
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
}
