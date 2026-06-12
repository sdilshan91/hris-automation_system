import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpRequest, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  ImportTemplateFormat,
  ImportResponse,
  IImportJobStatus,
} from '../models/bulk-import.models';

/**
 * US-CHR-010: Bulk Employee Import service.
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET  /api/v1/employees/import/template?format=csv|xlsx  - blob download
 *   POST /api/v1/employees/import                           - multipart file upload
 *   GET  /api/v1/employees/import/jobs/:jobId               - poll job status
 *   GET  /api/v1/employees/import/jobs/:jobId/error-report  - error report CSV blob
 */
@Injectable({ providedIn: 'root' })
export class BulkImportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/employees/import`;

  // ─── Step 1: Download template ────────────────────────────

  /**
   * Download the import template in CSV or Excel format.
   * Returns a Blob for client-side file download.
   */
  downloadTemplate(format: ImportTemplateFormat): Observable<Blob> {
    const params = new HttpParams().set('format', format);
    return this.http.get(`${this.baseUrl}/template`, {
      params,
      responseType: 'blob',
      withCredentials: true,
    });
  }

  // ─── Step 2: Upload import file ───────────────────────────

  /**
   * Upload a CSV or Excel file for bulk import (multipart/form-data).
   * Returns upload progress events when using reportProgress.
   *
   * The backend returns either:
   *   - IImportResult (sync, <= 500 rows)
   *   - IImportJobRef (async, > 500 rows)
   *   - 409 with IPlanLimitWarning if plan limit would be exceeded
   *
   * options.importUpToLimit: if true, tells backend to import up to the plan
   * limit even if the file exceeds it (AC-5 user choice).
   */
  uploadImport(
    file: File,
    options?: { importUpToLimit?: boolean }
  ): Observable<HttpEvent<ImportResponse>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    if (options?.importUpToLimit) {
      formData.append('importUpToLimit', 'true');
    }

    const req = new HttpRequest('POST', this.baseUrl, formData, {
      reportProgress: true,
      withCredentials: true,
    });

    return this.http.request<ImportResponse>(req);
  }

  // ─── Step 3: Async job status ─────────────────────────────

  /**
   * Poll the status of an async import job (AC-4).
   */
  getImportJobStatus(jobId: string): Observable<IImportJobStatus> {
    return this.http.get<IImportJobStatus>(`${this.baseUrl}/jobs/${jobId}`, {
      withCredentials: true,
    });
  }

  /**
   * Download the error report CSV for a completed import (FR-8).
   */
  downloadErrorReport(jobId: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/jobs/${jobId}/error-report`, {
      responseType: 'blob',
      withCredentials: true,
    });
  }
}
