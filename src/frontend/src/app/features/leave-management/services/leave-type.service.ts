import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ILeaveType,
  ICreateLeaveTypeRequest,
  IUpdateLeaveTypeRequest,
  IReorderLeaveTypesRequest,
  ILeaveTypeErrorResponse,
} from '../models/leave-type.models';

/**
 * US-LV-001: Service for leave type CRUD + reorder operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET    /api/v1/tenant/leave-types                - list all leave types for current tenant
 *   GET    /api/v1/tenant/leave-types/:id            - single leave type
 *   POST   /api/v1/tenant/leave-types                - create leave type
 *   PUT    /api/v1/tenant/leave-types/:id            - update leave type
 *   POST   /api/v1/tenant/leave-types/:id/deactivate - soft-deactivate (FR-5, AC-4)
 *   POST   /api/v1/tenant/leave-types/:id/reactivate  - reactivate
 *   POST   /api/v1/tenant/leave-types/reorder        - reorder display_order (FR-3)
 */
@Injectable({ providedIn: 'root' })
export class LeaveTypeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/leave-types`;

  // --- Read --------------------------------------------------

  /** Get all leave types for the current tenant, ordered by display_order */
  getLeaveTypes(): Observable<ILeaveType[]> {
    return this.http.get<ILeaveType[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** Get a single leave type by ID */
  getLeaveType(id: string): Observable<ILeaveType> {
    return this.http.get<ILeaveType>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  // --- Write -------------------------------------------------

  /** Create a new leave type (FR-1, FR-2) */
  createLeaveType(request: ICreateLeaveTypeRequest): Observable<ILeaveType> {
    return this.http.post<ILeaveType>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing leave type (FR-1) */
  updateLeaveType(
    id: string,
    request: IUpdateLeaveTypeRequest
  ): Observable<ILeaveType> {
    return this.http.put<ILeaveType>(
      `${this.baseUrl}/${id}`,
      request,
      { withCredentials: true }
    );
  }

  /** Deactivate a leave type (AC-4, FR-5) */
  deactivateLeaveType(id: string): Observable<ILeaveType> {
    return this.http.post<ILeaveType>(
      `${this.baseUrl}/${id}/deactivate`,
      {},
      { withCredentials: true }
    );
  }

  /** Reactivate a previously deactivated leave type */
  activateLeaveType(id: string): Observable<ILeaveType> {
    return this.http.post<ILeaveType>(
      `${this.baseUrl}/${id}/reactivate`,
      {},
      { withCredentials: true }
    );
  }

  /** Reorder leave types display order (FR-3) */
  reorderLeaveTypes(request: IReorderLeaveTypesRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reorder`, request, {
      withCredentials: true,
    });
  }

  /** Parse an error response into a typed leave type error. */
  static parseError(err: HttpErrorResponse): ILeaveTypeErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ILeaveTypeErrorResponse;
    }
    return null;
  }
}
