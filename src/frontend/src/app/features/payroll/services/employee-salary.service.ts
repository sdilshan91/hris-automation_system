import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ICtcBreakdown,
  IEmployeeCompensation,
  ISalaryAssignmentRequest,
  ISalaryAssignmentResult,
  ISalaryRevision,
  IBulkAssignmentRequest,
  IBulkAssignmentResult,
} from '../models/employee-salary.models';

/**
 * US-PAY-002: Service for assigning a salary structure to an employee — CTC
 * breakdown preview, single + bulk assignment, current compensation, and salary
 * revision history.
 *
 * Thin and isolated by design — the route strings live here ONLY, so a backend
 * contract change is a one-file fix (sibling to PayrollService from US-PAY-001).
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header). The backend stamps
 * tenant_id + audit fields and enforces RLS (FR-8, AC-5).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see employee-salary.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class EmployeeSalaryService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/payroll`;
  private readonly assignmentsUrl = `${this.base}/salary-assignments`;

  /**
   * Preview the CTC breakdown for an assignment BEFORE confirming (FR-3, AC-1).
   * The server computes each component from the structure's rules and the
   * declared CTC, applying any per-component overrides (AC-3), and returns the
   * FR-6 balanced flag. Same request shape as `assign` so what HR previews is
   * exactly what gets saved.
   */
  previewBreakdown(
    request: ISalaryAssignmentRequest,
  ): Observable<ICtcBreakdown> {
    return this.http.post<ICtcBreakdown>(
      `${this.assignmentsUrl}/preview`,
      request,
      { withCredentials: true },
    );
  }

  /** Assign a salary structure to one employee (FR-1, AC-1/AC-2/AC-3). */
  assign(
    request: ISalaryAssignmentRequest,
  ): Observable<ISalaryAssignmentResult> {
    return this.http.post<ISalaryAssignmentResult>(
      this.assignmentsUrl,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Bulk-assign one structure to multiple employees with individual CTCs (FR-5,
   * AC-4). Returns a per-row result so the UI can report partial success and the
   * progress indicator can settle on success/failure counts.
   */
  bulkAssign(
    request: IBulkAssignmentRequest,
  ): Observable<IBulkAssignmentResult> {
    return this.http.post<IBulkAssignmentResult>(
      `${this.assignmentsUrl}/bulk`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * The employee's CURRENT active compensation for the Compensation tab (§8).
   * When no structure is assigned the server returns a payload with null
   * structure/ctc so the UI can flag "Payroll Incomplete" (BR-5).
   */
  getCurrentCompensation(
    employeeId: string,
  ): Observable<IEmployeeCompensation> {
    return this.http.get<IEmployeeCompensation>(
      `${this.base}/employees/${employeeId}/compensation`,
      { withCredentials: true },
    );
  }

  /**
   * The employee's salary revision history, newest first (FR-4, BR-3). Tolerates
   * either a bare array or a `{ data }` page so a backend pagination choice does
   * not break the timeline.
   */
  getRevisionHistory(employeeId: string): Observable<ISalaryRevision[]> {
    return this.http
      .get<ISalaryRevision[] | { data: ISalaryRevision[] }>(
        `${this.base}/employees/${employeeId}/revision-history`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /** Accept either a bare array or a `{ data }` page; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
