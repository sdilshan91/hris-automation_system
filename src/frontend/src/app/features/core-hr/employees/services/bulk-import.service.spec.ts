import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpEventType } from '@angular/common/http';
import { BulkImportService } from './bulk-import.service';
import { IImportResult, IImportJobStatus } from '../models/bulk-import.models';
import { environment } from '../../../../../environments/environment';

describe('BulkImportService', () => {
  let service: BulkImportService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/employees/import`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        BulkImportService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(BulkImportService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── downloadTemplate ──────────────────────────────────────

  describe('downloadTemplate', () => {
    it('should GET CSV template as blob', () => {
      service.downloadTemplate('csv').subscribe((blob) => {
        expect(blob).toBeTruthy();
        expect(blob instanceof Blob).toBeTrue();
      });

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/template` && r.params.get('format') === 'csv'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(new Blob(['csv-data'], { type: 'text/csv' }));
    });

    it('should GET Excel template as blob', () => {
      service.downloadTemplate('xlsx').subscribe((blob) => {
        expect(blob).toBeTruthy();
      });

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/template` && r.params.get('format') === 'xlsx'
      );
      expect(req.request.method).toBe('GET');
      req.flush(new Blob(['xlsx-data'], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      }));
    });
  });

  // ─── uploadImport ──────────────────────────────────────────

  describe('uploadImport', () => {
    it('should POST file as multipart and report progress', () => {
      const mockFile = new File(['csv-data'], 'employees.csv', { type: 'text/csv' });
      const events: number[] = [];

      service.uploadImport(mockFile).subscribe((event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          events.push(Math.round((100 * event.loaded) / event.total));
        }
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ total: 10, success: 10, failed: 0, errors: [] });
    });

    it('should send importUpToLimit flag when set', () => {
      const mockFile = new File(['data'], 'employees.csv', { type: 'text/csv' });

      service.uploadImport(mockFile, { importUpToLimit: true }).subscribe();

      const req = httpMock.expectOne(baseUrl);
      const formData = req.request.body as FormData;
      expect(formData.get('importUpToLimit')).toBe('true');
      req.flush({ total: 5, success: 5, failed: 0, errors: [] });
    });

    it('should include file in FormData', () => {
      const mockFile = new File(['csv-data'], 'test.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });

      service.uploadImport(mockFile).subscribe();

      const req = httpMock.expectOne(baseUrl);
      const formData = req.request.body as FormData;
      expect(formData.get('file')).toBeTruthy();
      req.flush({ total: 1, success: 1, failed: 0, errors: [] });
    });
  });

  // ─── getImportJobStatus ────────────────────────────────────

  describe('getImportJobStatus', () => {
    it('should GET job status by ID', () => {
      const mockStatus: IImportJobStatus = {
        jobId: 'job-123',
        status: 'processing',
        progress: 45,
        result: null,
      };

      service.getImportJobStatus('job-123').subscribe((status) => {
        expect(status.jobId).toBe('job-123');
        expect(status.status).toBe('processing');
        expect(status.progress).toBe(45);
      });

      const req = httpMock.expectOne(`${baseUrl}/jobs/job-123`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockStatus);
    });

    it('should return completed status with result', () => {
      const mockResult: IImportResult = {
        total: 100,
        success: 95,
        failed: 5,
        errors: [{ row: 3, field: 'email', error: 'Duplicate email' }],
      };
      const mockStatus: IImportJobStatus = {
        jobId: 'job-456',
        status: 'completed',
        progress: 100,
        result: mockResult,
      };

      service.getImportJobStatus('job-456').subscribe((status) => {
        expect(status.status).toBe('completed');
        expect(status.result).toBeTruthy();
        expect(status.result!.success).toBe(95);
        expect(status.result!.errors.length).toBe(1);
      });

      const req = httpMock.expectOne(`${baseUrl}/jobs/job-456`);
      req.flush(mockStatus);
    });
  });

  // ─── downloadErrorReport ───────────────────────────────────

  describe('downloadErrorReport', () => {
    it('should GET error report as blob', () => {
      service.downloadErrorReport('job-789').subscribe((blob) => {
        expect(blob).toBeTruthy();
        expect(blob instanceof Blob).toBeTrue();
      });

      const req = httpMock.expectOne(`${baseUrl}/jobs/job-789/error-report`);
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(new Blob(['Row,Field,Error\n3,email,Duplicate'], { type: 'text/csv' }));
    });
  });
});
