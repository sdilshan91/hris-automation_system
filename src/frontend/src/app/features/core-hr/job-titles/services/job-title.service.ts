import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IJobTitle,
  ICreateJobTitleRequest,
  IUpdateJobTitleRequest,
  IEmploymentTypeDto,
  IEmploymentTypeOption,
} from '../models/job-title.models';

/**
 * US-CHR-005: Service for job title CRUD operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract):
 *   GET    /api/v1/job-titles           - list all job titles for current tenant
 *   GET    /api/v1/job-titles/:id       - single job title
 *   POST   /api/v1/job-titles           - create job title
 *   PUT    /api/v1/job-titles/:id       - update job title
 *   PATCH  /api/v1/job-titles/:id/deactivate - soft-deactivate (FR-5, FR-7)
 */
@Injectable({ providedIn: 'root' })
export class JobTitleService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/job-titles`;

  // --- Read --------------------------------------------------

  /** Get all job titles for the current tenant (FR-1) */
  getJobTitles(): Observable<IJobTitle[]> {
    return this.http.get<IJobTitle[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** Get a single job title by ID */
  getJobTitle(jobTitleId: string): Observable<IJobTitle> {
    return this.http.get<IJobTitle>(`${this.baseUrl}/${jobTitleId}`, {
      withCredentials: true,
    });
  }

  /**
   * DF-38: enumerate the employment-type options (exact enum member names + labels)
   * for the profile employment-type select. Sending an exact enum name avoids the
   * free-text 400 that System.Text.Json throws on an unrecognised value.
   */
  getEmploymentTypes(): Observable<IEmploymentTypeOption[]> {
    // The BE emits EmploymentTypeDto { id, name, displayName } (camelCase). Map to
    // the consumer's { value, label } contract, where `value` is the exact enum
    // member name (`name`) the BE binds on save and `label` is the display text.
    return this.http
      .get<IEmploymentTypeDto[]>(`${this.baseUrl}/employment-types`, {
        withCredentials: true,
      })
      .pipe(
        map((rows) =>
          rows.map((r) => ({ value: r.name, label: r.displayName }))
        )
      );
  }

  // --- Write -------------------------------------------------

  /** Create a new job title (FR-1, FR-2) */
  createJobTitle(request: ICreateJobTitleRequest): Observable<IJobTitle> {
    return this.http.post<IJobTitle>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing job title (FR-1) */
  updateJobTitle(
    jobTitleId: string,
    request: IUpdateJobTitleRequest
  ): Observable<IJobTitle> {
    return this.http.put<IJobTitle>(
      `${this.baseUrl}/${jobTitleId}`,
      request,
      { withCredentials: true }
    );
  }

  /** Deactivate (soft-delete) a job title (FR-5, FR-7) */
  deactivateJobTitle(jobTitleId: string): Observable<void> {
    return this.http.patch<void>(
      `${this.baseUrl}/${jobTitleId}/deactivate`,
      null,
      { withCredentials: true }
    );
  }
}
