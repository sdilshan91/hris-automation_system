import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IDepartment,
  ICreateDepartmentRequest,
  IUpdateDepartmentRequest,
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
    return this.http.get<IDepartment[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** Get a single department by ID */
  getDepartment(id: string): Observable<IDepartment> {
    return this.http.get<IDepartment>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  // ─── Write ───────────────────────────────────────────────

  /** Create a new department (FR-1, FR-2) */
  createDepartment(request: ICreateDepartmentRequest): Observable<IDepartment> {
    return this.http.post<IDepartment>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing department (FR-1, FR-4) */
  updateDepartment(
    id: string,
    request: IUpdateDepartmentRequest
  ): Observable<IDepartment> {
    return this.http.put<IDepartment>(
      `${this.baseUrl}/${id}`,
      request,
      { withCredentials: true }
    );
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
