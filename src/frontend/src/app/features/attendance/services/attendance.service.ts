import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IAttendanceLog,
  IClockInRequest,
  IClockStatus,
  IClockInErrorResponse,
  IClockOutRequest,
  IClockOutResult,
  ICreateRegularizationRequest,
  IRegularization,
  IRegularizationErrorResponse,
} from '../models/attendance.models';

/**
 * US-ATT-001: Service for the employee's self clock-in + today's clock status.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The
 * backend stamps tenant_id, IP, and user-agent server-side (FR-1, FR-5) — the
 * FE never sends them.
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET  /api/v1/attendance/status     - current employee's clock-in status today (IClockStatus)
 *   POST /api/v1/attendance/clock-in   - create a clock-in; returns IAttendanceLog
 *   POST /api/v1/attendance/clock-out  - close the open record; returns IClockOutResult (US-ATT-002)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is `${apiBaseUrl}/attendance`.
 */
@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/attendance`;

  // --- Read --------------------------------------------------

  /**
   * Get the current employee's clock-in status for today (FR-6, §8).
   * Used to initialise the card: already-clocked-in -> live timer (AC-2 reflect),
   * `requireGeolocation` -> AC-3 vs AC-4 branch, shift name/start -> context.
   */
  getStatus(): Observable<IClockStatus> {
    return this.http.get<IClockStatus>(`${this.baseUrl}/status`, {
      withCredentials: true,
    });
  }

  // --- Write -------------------------------------------------

  /** Submit a clock-in (FR-1, AC-1). Returns the created attendance log. */
  clockIn(request: IClockInRequest): Observable<IAttendanceLog> {
    return this.http.post<IAttendanceLog>(`${this.baseUrl}/clock-in`, request, {
      withCredentials: true,
    });
  }

  /**
   * US-ATT-002: Close the open attendance record (FR-1, AC-1). The backend sets
   * clock_out to the server UTC time (§10 — never client-reported), computes total
   * work minutes / overtime / status, and returns them in IClockOutResult.
   * AC-5: coordinates are included only when the tenant geo policy requires them.
   */
  clockOut(request: IClockOutRequest): Observable<IClockOutResult> {
    return this.http.post<IClockOutResult>(`${this.baseUrl}/clock-out`, request, {
      withCredentials: true,
    });
  }

  // --- US-ATT-003: Regularization ----------------------------

  /**
   * US-ATT-003 (FR-1, FR-2, AC-1/AC-2): submit an attendance regularization
   * request. Returns the created record with status 'PENDING'. Backend rejections
   * (AC-3 lookback, AC-4 duplicate pending, AC-5 locked payroll period) arrive as a
   * `{ message, code }` body — the caller displays the message verbatim.
   */
  submitRegularization(
    request: ICreateRegularizationRequest,
  ): Observable<IRegularization> {
    return this.http.post<IRegularization>(
      `${this.baseUrl}/regularizations`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * US-ATT-003 (§8): list the current employee's regularization requests with
   * their status, most-recent first (ordering owned by the backend). Tenant-scoped
   * via the tenantInterceptor; the employee is resolved from the JWT server-side.
   */
  listRegularizations(): Observable<IRegularization[]> {
    return this.http.get<IRegularization[]>(`${this.baseUrl}/regularizations`, {
      withCredentials: true,
    });
  }

  /** Parse a regularization error body (AC-3/AC-4/AC-5); shape matches clock-in. */
  static parseRegularizationError(
    err: HttpErrorResponse,
  ): IRegularizationErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IRegularizationErrorResponse;
    }
    return null;
  }

  // --- Error helper ------------------------------------------

  /** Parse an error response into a typed clock-in error (AC-2, AC-5, FR-3). */
  static parseError(err: HttpErrorResponse): IClockInErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IClockInErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from a clock-in error. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return AttendanceService.parseError(err)?.message ?? 'An unexpected error occurred.';
  }
}
