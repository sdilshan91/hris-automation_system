import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { environment } from '../../../../environments/environment';
import {
  ILeaveEncashmentRequest,
  ILeaveEncashmentResult,
  IReconciliationReport,
} from '../models/reconciliation.models';

/**
 * US-PAY-010: Attendance/leave → payroll integration service.
 *
 * Sibling to PayrollRunService / PayslipService; the route strings live here ONLY, so a
 * backend contract change is a one-file fix. The REST surface (base
 * `${apiBaseUrl}/payroll`, where apiBaseUrl already ends in `/api/v1`):
 *  - GET  /payroll/reconciliation?payYear=&payMonth=  → IReconciliationReport (FR-7)
 *  - POST /payroll/leave-encashments                  → ILeaveEncashmentResult (AC-3/FR-5)
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the methods consume BARE payloads. All calls are
 * withCredentials; tenant is carried by the tenantInterceptor (X-Tenant-Subdomain) and
 * isolation enforced server-side (RLS, AC-5/FR-8).
 */
@Injectable({ providedIn: 'root' })
export class ReconciliationService {
  private readonly http = inject(HttpClient);
  private readonly payrollUrl = `${environment.apiBaseUrl}/payroll`;
  private readonly reconciliationUrl = `${this.payrollUrl}/reconciliation`;
  private readonly encashmentUrl = `${this.payrollUrl}/leave-encashments`;

  /**
   * Fetch the pre-payroll reconciliation report for a period (FR-7). `payYear` +
   * `payMonth` are separate query params (ints). The payload carries the
   * `attendanceFinalized` flag (AC-4) and one row per employee.
   */
  getReconciliation(
    payYear: number,
    payMonth: number,
  ): Observable<IReconciliationReport> {
    const params = new HttpParams()
      .set('payYear', `${payYear}`)
      .set('payMonth', `${payMonth}`);
    return this.http
      .get<IReconciliationReport | { data: IReconciliationReport }>(
        this.reconciliationUrl,
        { params, withCredentials: true },
      )
      .pipe(map((res) => this.unwrap(res)));
  }

  /**
   * Trigger a leave encashment (AC-3/FR-5). The backend creates an earning adjustment on
   * the next available run for the target period and returns the calculated days/rate/
   * amount. `periodDeferred` in the result is true when the period was pushed forward.
   */
  triggerLeaveEncashment(
    request: ILeaveEncashmentRequest,
  ): Observable<ILeaveEncashmentResult> {
    return this.http.post<ILeaveEncashmentResult>(this.encashmentUrl, request, {
      withCredentials: true,
    });
  }

  /** Accept either a bare payload or a `{ data }` envelope (defensive, US-PLT-001). */
  private unwrap<T>(res: T | { data: T }): T {
    if (res && typeof res === 'object' && 'data' in (res as { data: T })) {
      return (res as { data: T }).data;
    }
    return res as T;
  }
}
