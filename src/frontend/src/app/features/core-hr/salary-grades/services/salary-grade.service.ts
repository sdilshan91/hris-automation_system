import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  ISalaryGrade,
  ISalaryGradeRequest,
  SalaryGradeWire,
  mapSalaryGrade,
} from '../models/salary-grade.models';

/**
 * ISSUE-021: Service for salary grade CRUD operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (PINNED contract):
 *   GET    /api/v1/tenant/salary-grades          - list (?includeInactive=)
 *   GET    /api/v1/tenant/salary-grades/:id       - single grade
 *   POST   /api/v1/tenant/salary-grades           - create
 *   PUT    /api/v1/tenant/salary-grades/:id        - update
 *   DELETE /api/v1/tenant/salary-grades/:id        - soft-delete (deactivate)
 */
@Injectable({ providedIn: 'root' })
export class SalaryGradeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/salary-grades`;

  // --- Read --------------------------------------------------

  /**
   * List salary grades for the current tenant. By default only active grades
   * are returned; pass `includeInactive` to include soft-deleted ones.
   */
  list(includeInactive = false): Observable<ISalaryGrade[]> {
    let params = new HttpParams();
    if (includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http
      .get<SalaryGradeWire[]>(this.baseUrl, {
        params,
        withCredentials: true,
      })
      .pipe(map((grades) => (grades ?? []).map(mapSalaryGrade)));
  }

  /** Get a single salary grade by ID */
  get(id: string): Observable<ISalaryGrade> {
    return this.http
      .get<SalaryGradeWire>(`${this.baseUrl}/${id}`, {
        withCredentials: true,
      })
      .pipe(map(mapSalaryGrade));
  }

  // --- Write -------------------------------------------------

  /** Create a new salary grade */
  create(request: ISalaryGradeRequest): Observable<ISalaryGrade> {
    return this.http
      .post<SalaryGradeWire>(this.baseUrl, request, {
        withCredentials: true,
      })
      .pipe(map(mapSalaryGrade));
  }

  /** Update an existing salary grade */
  update(
    id: string,
    request: ISalaryGradeRequest
  ): Observable<ISalaryGrade> {
    return this.http
      .put<SalaryGradeWire>(`${this.baseUrl}/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapSalaryGrade));
  }

  /** Deactivate (soft-delete) a salary grade */
  deactivate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }
}
