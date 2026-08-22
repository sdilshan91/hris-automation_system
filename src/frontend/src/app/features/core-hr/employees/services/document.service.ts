import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IEmployeeDocument,
  IUploadDocumentRequest,
} from '../models/document.models';
import { IPaginatedResponse } from '../models/employee.models';

/**
 * US-CHR-008: Service for employee document management operations.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel):
 *   GET    /api/v1/employees/:employeeId/documents             - list all documents
 *   POST   /api/v1/employees/:employeeId/documents             - upload document (multipart)
 *   GET    /api/v1/employees/:employeeId/documents/:id/download - get signed download URL
 *   DELETE /api/v1/employees/:employeeId/documents/:id          - soft-delete document
 */
@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/employees`;

  /**
   * List all documents for an employee.
   *
   * The backend returns a paginated `PagedResult` — `{ items, totalCount }`
   * (after `apiEnvelopeInterceptor` unwraps the `{ success, data }` envelope) —
   * NOT a bare array, and each item exposes its id as `id` (not `documentId`).
   * Unwrap `.items` and bridge `id → documentId` so the component receives the
   * `IEmployeeDocument[]` it expects (BUG-236: `.filter()` on the page object
   * threw `filter is not a function`; track/download/delete keyed off an
   * undefined `documentId`). Other fields already match by name.
   */
  getDocuments(employeeId: string): Observable<IEmployeeDocument[]> {
    return this.http
      .get<IPaginatedResponse<IEmployeeDocument>>(
        `${this.baseUrl}/${employeeId}/documents`,
        { withCredentials: true }
      )
      .pipe(
        map((page) =>
          (page.items ?? []).map((d) => ({
            ...d,
            documentId:
              d.documentId ?? (d as IEmployeeDocument & { id?: string }).id ?? '',
          }))
        )
      );
  }

  /**
   * Upload a document with metadata (FR-1).
   * Uses multipart/form-data. Reports upload progress via HttpEvent stream (NFR-1 UX).
   */
  uploadDocument(
    employeeId: string,
    file: File,
    metadata: IUploadDocumentRequest
  ): Observable<HttpEvent<IEmployeeDocument>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    formData.append('category', metadata.category);
    if (metadata.description) {
      formData.append('description', metadata.description);
    }
    if (metadata.expiryDate) {
      formData.append('expiryDate', metadata.expiryDate);
    }

    const req = new HttpRequest(
      'POST',
      `${this.baseUrl}/${employeeId}/documents`,
      formData,
      {
        reportProgress: true,
        withCredentials: true,
      }
    );
    return this.http.request<IEmployeeDocument>(req);
  }

  /**
   * Download a document (AC-4, FR-6) — returns the FILE, not a URL.
   *
   * GAP-027: this used to fetch `{ signedUrl }` and the page set it as an anchor href. The URL was
   * `/files/{tenantId}/{path}`, a scheme no route has ever served, so every Download click navigated to a
   * 404. The endpoint now streams the bytes, matching payslip / data-export / HR-report downloads, and this
   * reads them as a Blob — the same pattern the recommendation and leave exports already use.
   *
   * A blob is also the only way this can stay authenticated: a bare `/files/...` navigation cannot carry a
   * bearer token, which is precisely why real deployments use pre-signed URLs and why a half-built signing
   * scheme was worse than none.
   */
  downloadDocument(
    employeeId: string,
    documentId: string
  ): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/${employeeId}/documents/${documentId}/download`,
      { withCredentials: true, responseType: 'blob' }
    );
  }

  /**
   * Soft-delete a document (FR-7). HR Officer only.
   */
  deleteDocument(employeeId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/${employeeId}/documents/${documentId}`,
      { withCredentials: true }
    );
  }
}
