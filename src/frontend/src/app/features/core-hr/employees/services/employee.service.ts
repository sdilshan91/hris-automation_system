import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IEmployee,
  IEmployeeProfile,
  ICreateEmployeeRequest,
  IEmployeeErrorResponse,
  IUpdateSectionRequest,
  IUpdateSectionResponse,
  ProfileSection,
} from '../models/employee.models';

/**
 * US-CHR-001: Service for employee CRUD operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract):
 *   GET    /api/v1/employees           - list all employees for current tenant
 *   GET    /api/v1/employees/:id       - single employee
 *   POST   /api/v1/employees           - create employee (multipart for photo)
 *   PUT    /api/v1/employees/:id       - update employee
 *   DELETE /api/v1/employees/:id       - soft-delete employee
 *
 * US-CHR-002 additions (assumed contract — backend agent building in parallel):
 *   GET    /api/v1/employees/:id/profile          - full profile with sub-entities
 *   PATCH  /api/v1/employees/:id/sections/:section - per-section update with xmin concurrency
 */
@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/employees`;

  // ─── Read ────────────────────────────────────────────────

  /** Get all employees for the current tenant */
  getEmployees(): Observable<IEmployee[]> {
    return this.http.get<IEmployee[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** Get a single employee by ID */
  getEmployee(employeeId: string): Observable<IEmployee> {
    return this.http.get<IEmployee>(`${this.baseUrl}/${employeeId}`, {
      withCredentials: true,
    });
  }

  // ─── Write ───────────────────────────────────────────────

  /**
   * Create a new employee with optional profile photo (FR-1, FR-6, AC-4).
   *
   * Uses multipart/form-data when a photo is attached; JSON otherwise.
   * The backend auto-generates employee_no (FR-2) and sets tenant_id (FR-4).
   */
  createEmployee(
    request: ICreateEmployeeRequest,
    profilePhoto?: File | null
  ): Observable<IEmployee> {
    if (profilePhoto) {
      const formData = this.buildFormData(request, profilePhoto);
      return this.http.post<IEmployee>(this.baseUrl, formData, {
        withCredentials: true,
      });
    }

    return this.http.post<IEmployee>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  // ─── US-CHR-002: Profile ──────────────────────────────────

  /**
   * Get full employee profile with all sub-entities (AC-1).
   * Returns IEmployeeProfile including xmin concurrency token.
   *
   * Endpoint assumption: GET /api/v1/employees/:id/profile
   */
  getEmployeeProfile(employeeId: string): Observable<IEmployeeProfile> {
    return this.http.get<IEmployeeProfile>(
      `${this.baseUrl}/${employeeId}/profile`,
      { withCredentials: true }
    );
  }

  /**
   * Update a specific profile section (AC-2, AC-3, FR-2).
   * Sends xmin token for optimistic concurrency.
   * Backend returns 409 on stale xmin (AC-3).
   * Backend returns 403 if Employee role attempts restricted field edits (AC-5).
   *
   * Endpoint assumption: PATCH /api/v1/employees/:id/sections/:section
   */
  updateProfileSection(
    employeeId: string,
    section: ProfileSection,
    request: IUpdateSectionRequest
  ): Observable<IUpdateSectionResponse> {
    return this.http.patch<IUpdateSectionResponse>(
      `${this.baseUrl}/${employeeId}/sections/${section}`,
      request,
      { withCredentials: true }
    );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /**
   * Build a FormData object for multipart submission.
   * Appends all non-null request fields + the photo file.
   */
  private buildFormData(
    request: ICreateEmployeeRequest,
    photo: File
  ): FormData {
    const fd = new FormData();

    // Append all string fields
    const entries = Object.entries(request) as [string, unknown][];
    for (const [key, value] of entries) {
      if (value === null || value === undefined) continue;
      if (typeof value === 'object') {
        fd.append(key, JSON.stringify(value));
      } else {
        fd.append(key, String(value));
      }
    }

    fd.append('profilePhoto', photo, photo.name);
    return fd;
  }

  /**
   * Parse an error response into a typed employee error.
   * Returns null if the error doesn't match the expected shape.
   */
  static parseError(err: HttpErrorResponse): IEmployeeErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IEmployeeErrorResponse;
    }
    return null;
  }
}
