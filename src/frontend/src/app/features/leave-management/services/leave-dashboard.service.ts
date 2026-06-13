import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILeaveBalanceSummary,
  ILeaveLedgerEntry,
} from '../models/leave-dashboard.models';
import { ILeaveRequest } from '../models/leave-request.models';

/**
 * US-LV-006: Service for the employee's Leave Balance Dashboard.
 *
 * A sibling to LeaveRequestService (US-LV-003), targeting the same employee-facing
 * `/leaves` resource but the dashboard-specific read endpoints. All requests are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header) and carry
 * withCredentials for httpOnly cookie auth, matching the established pattern.
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET /api/v1/leaves/my-balance?year={year}                 - balances per type (FR-1, FR-2)
 *   GET /api/v1/leaves/my-ledger?leaveTypeId={id}&year={year} - transaction log (FR-3)
 *   GET /api/v1/leaves/my-upcoming                            - approved+pending future (FR-4)
 *
 * Past-request history (FR-6) is served by LeaveRequestService.getMyLeaveRequests
 * (GET /leaves/mine) -- this service does NOT duplicate that call.
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource base is `${apiBaseUrl}/leaves`.
 */
@Injectable({ providedIn: 'root' })
export class LeaveDashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  /**
   * Get the employee's leave-type balances for the given leave year (FR-1, FR-2).
   * An empty array signals the AC-5 empty state (no balances configured yet).
   */
  getMyBalance(year: number): Observable<ILeaveBalanceSummary[]> {
    const params = new HttpParams().set('year', String(year));
    return this.http.get<ILeaveBalanceSummary[]>(`${this.baseUrl}/my-balance`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * Get the full transaction ledger for one leave type in the given year (FR-3, AC-2).
   * Ordered by occurredAt by the backend.
   */
  getMyLedger(leaveTypeId: string, year: number): Observable<ILeaveLedgerEntry[]> {
    const params = new HttpParams()
      .set('leaveTypeId', leaveTypeId)
      .set('year', String(year));
    return this.http.get<ILeaveLedgerEntry[]>(`${this.baseUrl}/my-ledger`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * Get the employee's approved + pending future leaves for the timeline (FR-4, AC-3).
   * Returns the same ILeaveRequest shape used by the My Requests list.
   */
  getMyUpcoming(): Observable<ILeaveRequest[]> {
    return this.http.get<ILeaveRequest[]>(`${this.baseUrl}/my-upcoming`, {
      withCredentials: true,
    });
  }
}
