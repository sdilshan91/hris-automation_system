import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpEventType } from '@angular/common/http';
import { DocumentService } from './document.service';
import {
  IEmployeeDocument,
  IDocumentDownloadResponse,
  IUploadDocumentRequest,
} from '../models/document.models';
import { environment } from '../../../../../environments/environment';

describe('DocumentService', () => {
  let service: DocumentService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/employees`;
  const employeeId = 'emp-1';
  const docUrl = `${baseUrl}/${employeeId}/documents`;

  const mockDocument: IEmployeeDocument = {
    documentId: 'doc-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    fileName: 'contract.pdf',
    storageKey: 'tenant-1/core-hr/emp-1/2026/06/contract.pdf',
    fileSizeBytes: 2048000,
    mimeType: 'application/pdf',
    category: 'Contract',
    description: 'Employment contract',
    expiryDate: '2027-12-31',
    uploadedBy: 'user-1',
    uploadedByName: 'Admin User',
    createdAt: '2026-06-12T10:00:00Z',
    updatedAt: '2026-06-12T10:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DocumentService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(DocumentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getDocuments', () => {
    it('should return all documents for an employee', () => {
      service.getDocuments(employeeId).subscribe((docs) => {
        expect(docs.length).toBe(1);
        expect(docs[0].fileName).toBe('contract.pdf');
        expect(docs[0].category).toBe('Contract');
      });

      const req = httpMock.expectOne(docUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockDocument]);
    });

    it('should return empty array when no documents exist', () => {
      service.getDocuments(employeeId).subscribe((docs) => {
        expect(docs.length).toBe(0);
      });

      const req = httpMock.expectOne(docUrl);
      req.flush([]);
    });
  });

  describe('uploadDocument', () => {
    it('should POST multipart form data with file and metadata', () => {
      const file = new File(['file-content'], 'test.pdf', { type: 'application/pdf' });
      const metadata: IUploadDocumentRequest = {
        category: 'Contract',
        description: 'Test doc',
        expiryDate: '2027-01-01',
      };

      service.uploadDocument(employeeId, file, metadata).subscribe((event) => {
        if (event.type === HttpEventType.Response) {
          expect(event.body?.fileName).toBe('contract.pdf');
        }
      });

      const req = httpMock.expectOne(docUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();

      const formData = req.request.body as FormData;
      expect(formData.get('category')).toBe('Contract');
      expect(formData.get('description')).toBe('Test doc');
      expect(formData.get('expiryDate')).toBe('2027-01-01');
      expect(formData.get('file')).toBeTruthy();

      req.flush(mockDocument);
    });

    it('should omit optional fields when not provided', () => {
      const file = new File(['data'], 'id.jpg', { type: 'image/jpeg' });
      const metadata: IUploadDocumentRequest = {
        category: 'ID',
      };

      service.uploadDocument(employeeId, file, metadata).subscribe();

      const req = httpMock.expectOne(docUrl);
      const formData = req.request.body as FormData;
      expect(formData.get('category')).toBe('ID');
      expect(formData.has('description')).toBeFalse();
      expect(formData.has('expiryDate')).toBeFalse();
      req.flush(mockDocument);
    });
  });

  describe('getDownloadUrl', () => {
    it('should GET the signed download URL for a document', () => {
      const downloadResponse: IDocumentDownloadResponse = {
        downloadUrl: 'https://storage.example.com/signed-url?token=abc',
        expiresAt: '2026-06-12T10:05:00Z',
      };

      service.getDownloadUrl(employeeId, 'doc-1').subscribe((resp) => {
        expect(resp.downloadUrl).toContain('signed-url');
        expect(resp.expiresAt).toBeTruthy();
      });

      const req = httpMock.expectOne(`${docUrl}/doc-1/download`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(downloadResponse);
    });
  });

  describe('deleteDocument', () => {
    it('should DELETE a document by ID', () => {
      service.deleteDocument(employeeId, 'doc-1').subscribe();

      const req = httpMock.expectOne(`${docUrl}/doc-1`);
      expect(req.request.method).toBe('DELETE');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});
