import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IJobTitle,
  ICreateJobTitleRequest,
  IUpdateJobTitleRequest,
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
  private readonly baseUrl = `${environment.apiBaseUrl}/job-titles`;

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
