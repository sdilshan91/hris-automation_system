import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IDepartment,
  ICreateDepartmentRequest,
  IUpdateDepartmentRequest,
  DepartmentWire,
  mapDepartment,
} from '../models/department.models';

/**
 * US-CHR-004: Service for department CRUD operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract):
 *   GET    /api/v1/departments           — list all departments for current tenant
 *   GET    /api/v1/departments/:id       — single department
 *   POST   /api/v1/departments           — create department
 *   PUT    /api/v1/departments/:id       — update department
 *   PATCH  /api/v1/departments/:id/deactivate — soft-deactivate (FR-7)
 */
@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/departments`;

  // ─── Read ────────────────────────────────────────────────

  /** Get all departments for the current tenant (FR-1, FR-8) */
  getDepartments(): Observable<IDepartment[]> {
    // The list endpoint returns IReadOnlyList<DepartmentsDepartmentDto> (a bare array after the envelope
    // interceptor strips ApiResponse<T>); map each row through the contract-anchored mapper.
    return this.http
      .get<DepartmentWire[]>(this.baseUrl, { withCredentials: true })
      .pipe(map((rows) => rows.map(mapDepartment)));
  }

  /** Get a single department by ID */
  getDepartment(id: string): Observable<IDepartment> {
    return this.http
      .get<DepartmentWire>(`${this.baseUrl}/${id}`, { withCredentials: true })
      .pipe(map(mapDepartment));
  }

  // ─── Write ───────────────────────────────────────────────

  /** Create a new department (FR-1, FR-2) */
  createDepartment(request: ICreateDepartmentRequest): Observable<IDepartment> {
    return this.http
      .post<DepartmentWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapDepartment));
  }

  /** Update an existing department (FR-1, FR-4) */
  updateDepartment(
    id: string,
    request: IUpdateDepartmentRequest
  ): Observable<IDepartment> {
    return this.http
      .put<DepartmentWire>(`${this.baseUrl}/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapDepartment));
  }

  /** Deactivate (soft-delete) a department (FR-6, FR-7) */
  deactivateDepartment(id: string): Observable<void> {
    // GAP-014: the API exposes deactivate as POST /{id}/deactivate; PATCH returned 405 for every call.
    return this.http.post<void>(
      `${this.baseUrl}/${id}/deactivate`,
      null,
      { withCredentials: true }
    );
  }
}
