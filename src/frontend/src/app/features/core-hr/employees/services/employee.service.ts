import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  EmployeeDirectoryWire,
  mapEmployeeDirectory,
  IEmployee,
  IEmployeeProfile,
  ICreateEmployeeRequest,
  IEmployeeErrorResponse,
  IUpdateEmployeeProfileRequest,
  IEmployeeDirectoryParams,
  IPaginatedResponse,
  ExportFormat,
  IStatusTransition,
  IChangeStatusRequest,
  IChangeStatusResponse,
  IAssignManagerRequest,
  IAssignManagerResponse,
  IDirectReport,
  IBulkAssignManagerRequest,
  IBulkAssignManagerResponse,
  IRevealNationalIdResponse,
  EmployeeWire,
  EmployeeListWire,
  DirectReportsResultWire,
  ValidTransitionsWire,
  BulkAssignManagerResultWire,
  NationalIdRevealWire,
  mapEmployee,
  mapEmployeeList,
  mapDirectReports,
  mapValidTransitions,
  mapBulkAssignManager,
  mapNationalIdReveal,
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
 * US-CHR-002 additions:
 *   GET    /api/v1/employees/:id/profile - full profile with sub-entities
 *   PATCH  /api/v1/employees/:id/profile - update profile fields with `rowVersion` concurrency
 *     (DF-36/ISSUE-319: there is NO per-section `sections/:section` route)
 */
@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/employees`;

  // ─── Read ────────────────────────────────────────────────

  /**
   * Get all employees for the current tenant.
   * The endpoint returns `EmployeesEmployeeListResult` (an envelope), so the bare
   * `IEmployee[]` is projected off `.items` through the contract mapper.
   */
  getEmployees(): Observable<IEmployee[]> {
    return this.http
      .get<EmployeeListWire>(this.baseUrl, { withCredentials: true })
      .pipe(map((w) => (w.items ?? []).map(mapEmployee)));
  }

  /** Get a single employee by ID */
  getEmployee(employeeId: string): Observable<IEmployee> {
    return this.http
      .get<EmployeeWire>(`${this.baseUrl}/${employeeId}`, {
        withCredentials: true,
      })
      .pipe(map(mapEmployee));
  }

  // ─── Directory (US-CHR-003) ──────────────────────────────

  /**
   * Query the employee directory with search, filters, sort, and pagination.
   * Backend contract: GET /api/v1/tenant/employees
   * Query params: search, departments (csv), jobTitles (csv), statuses (csv),
   *   employmentTypes (csv), location, dateOfJoiningFrom, dateOfJoiningTo,
   *   sort, sortDirection, page, pageSize, includeArchived
   * Response: { data: IEmployee[], total: number, page: number, pageSize: number }
   */
  queryDirectory(
    params: IEmployeeDirectoryParams
  ): Observable<IPaginatedResponse<IEmployee>> {
    const httpParams = this.buildDirectoryParams(params);
    // `/directory`, NOT the bare list route. The list endpoint accepts only page/pageSize/activeOnly/
    // search/includeTerminated, so every departmental, status, employment-type, location and date filter
    // the screen builds was being discarded server-side. Different endpoint, different envelope
    // ({data,total} vs {items,totalCount}) -- hence a distinct wire type and mapper.
    return this.http
      .get<EmployeeDirectoryWire>(`${this.baseUrl}/directory`, {
        params: httpParams,
        withCredentials: true,
      })
      .pipe(map(mapEmployeeDirectory));
  }

  /**
   * Export the filtered employee directory as CSV or Excel (AC-5, FR-8).
   *
   * Route: `GET /api/v1/tenant/employees/directory/export`.
   *
   * **This used to call `/employees/export`, which does not exist in the contract** — the export button
   * 404'd. Corrected 2026-08-21 alongside the directory filter routing below; both were found by the
   * D-core-hr migration, which typed the responses against the generated contract and made the mismatch
   * visible.
   */
  exportDirectory(
    params: IEmployeeDirectoryParams,
    format: ExportFormat
  ): Observable<Blob> {
    let httpParams = this.buildDirectoryParams(params);
    httpParams = httpParams.set('format', format);
    return this.http.get(`${this.baseUrl}/directory/export`, {
      params: httpParams,
      responseType: 'blob',
      withCredentials: true,
    });
  }

  /**
   * Build HttpParams from directory query parameters.
   * Multi-select arrays are sent as comma-separated values.
   */
  buildDirectoryParams(params: IEmployeeDirectoryParams): HttpParams {
    let httpParams = new HttpParams();

    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    // Multi-selects are APPENDED as repeated params (?departmentIds=a&departmentIds=b), not comma-joined.
    // ASP.NET binds a string[] query parameter from repeated keys; a single comma-joined value binds as ONE
    // element containing a comma, so every multi-select silently matched nothing. Names corrected too:
    // the contract expects departmentIds / locations (plural), not departments / location.
    for (const id of params.departments ?? []) {
      httpParams = httpParams.append('departmentIds', id);
    }
    for (const t of params.jobTitles ?? []) {
      httpParams = httpParams.append('jobTitles', t);
    }
    for (const st of params.statuses ?? []) {
      httpParams = httpParams.append('statuses', st);
    }
    if (params.employmentTypes?.length) {
      for (const et of params.employmentTypes) {
        httpParams = httpParams.append('employmentTypes', et);
      }
    }
    if (params.location) {
      httpParams = httpParams.append('locations', params.location);
    }
    if (params.dateOfJoiningFrom) {
      httpParams = httpParams.set('dateOfJoiningFrom', params.dateOfJoiningFrom);
    }
    if (params.dateOfJoiningTo) {
      httpParams = httpParams.set('dateOfJoiningTo', params.dateOfJoiningTo);
    }
    if (params.sort) {
      httpParams = httpParams.set('sort', params.sort);
    }
    if (params.sortDirection) {
      httpParams = httpParams.set('sortDirection', params.sortDirection);
    }
    if (params.page != null) {
      httpParams = httpParams.set('page', params.page.toString());
    }
    if (params.pageSize != null) {
      httpParams = httpParams.set('pageSize', params.pageSize.toString());
    }
    if (params.includeArchived) {
      // contract name is showArchived; includeArchived was ignored
      httpParams = httpParams.set('showArchived', 'true');
    }

    return httpParams;
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
      return this.http
        .post<EmployeeWire>(this.baseUrl, formData, { withCredentials: true })
        .pipe(map(mapEmployee));
    }

    return this.http
      .post<EmployeeWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapEmployee));
  }

  // ─── US-CHR-002: Profile ──────────────────────────────────

  /**
   * Get full employee profile with all sub-entities (AC-1).
   * Returns IEmployeeProfile including the `rowVersion` concurrency token (from the generated contract).
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
   * Update editable profile fields (AC-2, AC-3, FR-2).
   *
   * Backend contract: PATCH /api/v1/tenant/employees/:id/profile with an
   * `UpdateEmployeeProfileRequest` body. Only the section(s) present in the
   * request are applied. The `rowVersion` (numeric, Postgres xmin under the hood) drives optimistic
   * concurrency — the backend returns 409 on a stale token (AC-3) and 403 when
   * an Employee-role user edits restricted fields (AC-5).
   *
   * DF-36/ISSUE-319: this replaces the old `updateProfileSection` which PATCHed a
   * non-existent `sections/:section` route (every inline save 404'd).
   *
   * Response is `ApiResponse<EmployeeProfileDto>`; the `apiEnvelopeInterceptor`
   * unwraps the envelope, so this stream emits the updated `IEmployeeProfile`
   * (mirrors `getEmployeeProfile`).
   */
  updateEmployeeProfile(
    employeeId: string,
    request: IUpdateEmployeeProfileRequest
  ): Observable<IEmployeeProfile> {
    return this.http.patch<IEmployeeProfile>(
      `${this.baseUrl}/${employeeId}/profile`,
      request,
      { withCredentials: true }
    );
  }

  /**
   * ISSUE-293: Reveal the full (decrypted) National ID for an employee.
   * Backend contract: GET /api/v1/tenant/employees/:id/national-id
   * Gated by the `Employee.View.All` permission and audited server-side on
   * every call. Response is ApiResponse<{ nationalId }> — the `apiEnvelopeInterceptor`
   * unwraps the envelope, so this stream emits the inner `{ nationalId }` shape.
   */
  revealNationalId(employeeId: string): Observable<IRevealNationalIdResponse> {
    return this.http
      .get<NationalIdRevealWire>(`${this.baseUrl}/${employeeId}/national-id`, {
        withCredentials: true,
      })
      .pipe(map(mapNationalIdReveal));
  }

  // ─── US-CHR-009: Status Management ─────────────────────────

  /**
   * Get valid status transitions for an employee (AC-1, BR-1).
   * Backend is the source of truth — the frontend does NOT hardcode the matrix.
   *
   * Endpoint assumption: GET /api/v1/tenant/employees/:id/status/transitions
   * Returns: IStatusTransition[] — only the transitions valid from the current status.
   */
  getValidTransitions(employeeId: string): Observable<IStatusTransition[]> {
    return this.http
      .get<ValidTransitionsWire>(
        `${this.baseUrl}/${employeeId}/status/transitions`,
        { withCredentials: true }
      )
      .pipe(map(mapValidTransitions));
  }

  /**
   * Change an employee's status (AC-2, FR-3, NFR-3).
   * Sends an Idempotency-Key header to prevent duplicate transitions on retry.
   *
   * Endpoint assumption: POST /api/v1/tenant/employees/:id/status
   * Body: { newStatus, effectiveDate, reason }
   * Header: Idempotency-Key: <uuid>
   * Returns 200 with updated profile on success.
   * Returns 400 with error message on invalid transition (AC-5).
   */
  changeStatus(
    employeeId: string,
    request: IChangeStatusRequest,
    idempotencyKey: string
  ): Observable<IChangeStatusResponse> {
    return this.http.post<IChangeStatusResponse>(
      `${this.baseUrl}/${employeeId}/status`,
      request,
      {
        withCredentials: true,
        headers: { 'Idempotency-Key': idempotencyKey },
      }
    );
  }

  // ─── US-CHR-011: Reporting Structure ──────────────────────

  /**
   * Assign a reporting manager to an employee (AC-1, AC-2).
   * Pass null managerEmployeeId to remove the manager assignment (FR-8).
   * Backend detects circular chains and returns 400 (AC-3).
   *
   * Endpoint assumption: POST /api/v1/tenant/employees/:id/manager
   */
  assignManager(
    employeeId: string,
    managerEmployeeId: string | null
  ): Observable<IAssignManagerResponse> {
    const body: IAssignManagerRequest = { managerEmployeeId };
    return this.http.post<IAssignManagerResponse>(
      `${this.baseUrl}/${employeeId}/manager`,
      body,
      { withCredentials: true }
    );
  }

  /**
   * Get direct reports for a manager (AC-4, FR-5).
   *
   * Endpoint assumption: GET /api/v1/tenant/employees/:managerId/direct-reports
   */
  getDirectReports(managerId: string): Observable<IDirectReport[]> {
    return this.http
      .get<DirectReportsResultWire>(
        `${this.baseUrl}/${managerId}/direct-reports`,
        { withCredentials: true }
      )
      .pipe(map(mapDirectReports));
  }

  /**
   * Bulk assign a manager to multiple employees (AC-5, FR-4).
   * Returns per-employee success/failure results.
   *
   * Endpoint assumption: POST /api/v1/tenant/employees/bulk-assign-manager
   */
  bulkAssignManager(
    request: IBulkAssignManagerRequest
  ): Observable<IBulkAssignManagerResponse> {
    return this.http
      .post<BulkAssignManagerResultWire>(
        `${this.baseUrl}/bulk-assign-manager`,
        request,
        { withCredentials: true }
      )
      .pipe(map(mapBulkAssignManager));
  }

  /**
   * Search active employees for manager autocomplete (AC-1).
   * Reuses the directory endpoint with status=Active filter.
   */
  searchActiveEmployees(
    search: string,
    pageSize = 10
  ): Observable<IPaginatedResponse<IEmployee>> {
    // `activeOnly`, not `statuses`. This is the LIST endpoint (correct for a lightweight typeahead), and it
    // has no `statuses` parameter -- so the active-only restriction was silently dropped and the picker
    // offered terminated employees. Six call sites rely on this, including the payroll adjustment and
    // leave-encashment forms, where selecting a terminated employee is a real hazard.
    let params = new HttpParams()
      .set('search', search)
      .set('activeOnly', 'true')
      .set('page', '1')
      .set('pageSize', pageSize.toString());
    return this.http
      .get<EmployeeListWire>(this.baseUrl, {
        params,
        withCredentials: true,
      })
      .pipe(map(mapEmployeeList));
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
