import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IEmployeeDocument,
  IUploadDocumentRequest,
  IDocumentDownloadResponse,
} from '../models/document.models';

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
  private readonly baseUrl = `${environment.apiBaseUrl}/employees`;

  /**
   * List all documents for an employee.
   * Backend returns IEmployeeDocument[] (tenant-scoped via interceptor).
   */
  getDocuments(employeeId: string): Observable<IEmployeeDocument[]> {
    return this.http.get<IEmployeeDocument[]>(
      `${this.baseUrl}/${employeeId}/documents`,
      { withCredentials: true }
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
   * Get a short-lived signed download URL for a document (AC-4, FR-6).
   * The caller should follow the returned URL to download the file.
   */
  getDownloadUrl(
    employeeId: string,
    documentId: string
  ): Observable<IDocumentDownloadResponse> {
    return this.http.get<IDocumentDownloadResponse>(
      `${this.baseUrl}/${employeeId}/documents/${documentId}/download`,
      { withCredentials: true }
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
