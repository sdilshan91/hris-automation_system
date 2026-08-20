import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILopEntry,
  IAssignLopRequest,
  IAssignLopResult,
  IAssignCompulsoryLeaveRequest,
  IAssignCompulsoryLeaveResult,
  IOverrideLopRequest,
  IOverrideLopResult,
  ILopErrorResponse,
  LopRegisterWire,
  mapLopRegisterEntry,
  AssignLopResultWire,
  mapAssignLopResult,
  CompulsoryLeaveResultWire,
  mapCompulsoryLeaveResult,
  OverrideLopResultWire,
  mapOverrideLopResult,
} from '../models/lop.models';

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
 * Backend endpoints the service calls:
 *   GET  /api/v1/leaves/lop-register?from&to&employeeIds  - cross-employee LOP register (FR-5)
 *   POST /api/v1/leaves/assign-lop                        - bulk LOP assign (FR-3)
 *   POST /api/v1/leaves/compulsory                        - compulsory leave (FR-6)
 *   POST /api/v1/leaves/lop/{id}/override                 - override system LOP (BR-3)
 *
 * (The per-employee, payroll-facing `GET /leaves/lop-summary` still exists on the backend for payroll but is
 * no longer called from the FE — the register endpoint above is what the HR management screen needs.)
 */
@Injectable({ providedIn: 'root' })
export class LopService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves`;

  // --- Read --------------------------------------------------

  /**
   * Cross-employee LOP register for a period (FR-4/FR-5) — what the LOP management screen actually needs.
   *
   * **This is a different endpoint from the payroll `lop-summary` read on purpose.** `lop-summary` is
   * per-employee and built for payroll: it requires `employeeId`+`from`+`to` and 400s without them, and its
   * rows carry no employee identity. The register returns one row per LOP occurrence **across employees**,
   * each with `employeeName`/`employeeNo`, which is what the table renders and what the per-row Override acts on.
   *
   * The response is typed as the GENERATED contract type and mapped explicitly, so a backend rename is a
   * compile error rather than a blank column.
   */
  getLopRegister(from: string, to: string, employeeIds?: string[]): Observable<ILopEntry[]> {
    let httpParams = new HttpParams().set('from', from).set('to', to);
    for (const id of employeeIds ?? []) {
      httpParams = httpParams.append('employeeIds', id);
    }
    return this.http
      .get<LopRegisterWire[]>(`${this.baseUrl}/lop-register`, {
        params: httpParams,
        withCredentials: true,
      })
      .pipe(map((rows) => (rows ?? []).map(mapLopRegisterEntry)));
  }

  // --- Write -------------------------------------------------

  /** Bulk-assign LOP days to one employee (FR-3, AC-3). */
  assignLop(request: IAssignLopRequest): Observable<IAssignLopResult> {
    return this.http
      .post<AssignLopResultWire>(`${this.baseUrl}/assign-lop`, request, {
        withCredentials: true,
      })
      .pipe(map(mapAssignLopResult));
  }

  /**
   * Assign compulsory leave (company shutdown) to all / selected employees (FR-6).
   * The backend deducts from balance first, falling back to LOP (BR-4).
   */
  assignCompulsoryLeave(
    request: IAssignCompulsoryLeaveRequest,
  ): Observable<IAssignCompulsoryLeaveResult> {
    return this.http
      .post<CompulsoryLeaveResultWire>(`${this.baseUrl}/compulsory`, request, {
        withCredentials: true,
      })
      .pipe(map(mapCompulsoryLeaveResult));
  }

  /**
   * Override a system-generated LOP entry by converting it to a different leave
   * type (BR-3). Returns the small override result (not a full leave request).
   */
  overrideLop(
    leaveRequestId: string,
    request: IOverrideLopRequest,
  ): Observable<IOverrideLopResult> {
    return this.http
      .post<OverrideLopResultWire>(
        `${this.baseUrl}/lop/${leaveRequestId}/override`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapOverrideLopResult));
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
