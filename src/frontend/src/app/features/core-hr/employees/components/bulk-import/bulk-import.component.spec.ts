import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { HttpResponse } from '@angular/common/http';
import {
  BulkImportComponent,
  validateImportFile,
  generateErrorReportCsv,
} from './bulk-import.component';
import {
  IImportResult,
  IImportRowError,
  getImportOutcome,
  isImportResult,
  isImportJobRef,
  isPlanLimitWarning,
  MAX_IMPORT_FILE_SIZE_BYTES,
} from '../../models/bulk-import.models';
import { environment } from '../../../../../../environments/environment';

// ─── Pure-function tests (NO TestBed, NO httpMock.verify()) ──

describe('BulkImport pure functions', () => {

  describe('validateImportFile', () => {
    it('should return null for a valid .csv file', () => {
      const file = new File(['data'], 'employees.csv', { type: 'text/csv' });
      expect(validateImportFile(file)).toBeNull();
    });

    it('should return null for a valid .xlsx file', () => {
      const file = new File(['data'], 'employees.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      expect(validateImportFile(file)).toBeNull();
    });

    it('should reject .pdf files', () => {
      const file = new File(['data'], 'employees.pdf', { type: 'application/pdf' });
      const error = validateImportFile(file);
      expect(error).toBeTruthy();
      expect(error).toContain('.csv');
    });

    it('should reject .xls files', () => {
      const file = new File(['data'], 'old.xls', { type: 'application/vnd.ms-excel' });
      const error = validateImportFile(file);
      expect(error).toBeTruthy();
      expect(error).toContain('.csv');
    });

    it('should reject files exceeding 25 MB', () => {
      // Create a file object with a size getter that returns > 25 MB
      const bigContent = new ArrayBuffer(1); // actual content doesn't matter
      const file = new File([bigContent], 'huge.csv', { type: 'text/csv' });
      // Override size via Object.defineProperty since File.size is read-only
      Object.defineProperty(file, 'size', { value: MAX_IMPORT_FILE_SIZE_BYTES + 1 });
      const error = validateImportFile(file);
      expect(error).toBeTruthy();
      expect(error).toContain('25 MB');
    });

    it('should reject empty files', () => {
      const file = new File([], 'empty.csv', { type: 'text/csv' });
      const error = validateImportFile(file);
      expect(error).toBeTruthy();
      expect(error).toContain('empty');
    });
  });

  describe('generateErrorReportCsv', () => {
    it('should generate CSV with header and rows', () => {
      const errors: IImportRowError[] = [
        { row: 3, field: 'email', error: 'Duplicate email' },
        { row: 7, field: 'department_name', error: 'Department not found' },
      ];
      const csv = generateErrorReportCsv(errors);
      const lines = csv.split('\n');
      expect(lines[0]).toBe('Row,Field,Error');
      expect(lines.length).toBe(3);
      expect(lines[1]).toContain('3');
      expect(lines[1]).toContain('email');
      expect(lines[1]).toContain('Duplicate email');
    });

    it('should escape double quotes in CSV fields', () => {
      const errors: IImportRowError[] = [
        { row: 1, field: 'name', error: 'Contains "quotes"' },
      ];
      const csv = generateErrorReportCsv(errors);
      expect(csv).toContain('""quotes""');
    });

    it('should return only header for empty errors array', () => {
      const csv = generateErrorReportCsv([]);
      expect(csv).toBe('Row,Field,Error');
    });
  });

  describe('getImportOutcome', () => {
    it('should return all-success when failed is 0', () => {
      const result: IImportResult = { total: 10, success: 10, failed: 0, errors: [] };
      expect(getImportOutcome(result)).toBe('all-success');
    });

    it('should return all-failed when success is 0', () => {
      const result: IImportResult = {
        total: 5, success: 0, failed: 5,
        errors: [{ row: 1, field: 'email', error: 'bad' }],
      };
      expect(getImportOutcome(result)).toBe('all-failed');
    });

    it('should return partial when both success and failed > 0', () => {
      const result: IImportResult = {
        total: 10, success: 8, failed: 2,
        errors: [{ row: 1, field: 'email', error: 'bad' }],
      };
      expect(getImportOutcome(result)).toBe('partial');
    });
  });

  describe('type guards', () => {
    it('isImportResult should return true for sync result', () => {
      expect(isImportResult({ total: 5, success: 5, failed: 0, errors: [] })).toBeTrue();
    });

    it('isImportResult should return false for async ref', () => {
      expect(isImportResult({ jobId: 'j1', status: 'queued' } as any)).toBeFalse();
    });

    it('isImportJobRef should return true for async ref', () => {
      expect(isImportJobRef({ jobId: 'j1', status: 'queued' })).toBeTrue();
    });

    it('isPlanLimitWarning should return true for plan limit body', () => {
      expect(
        isPlanLimitWarning({
          code: 'plan_limit_exceeded',
          message: 'Limit reached',
          maxAllowed: 100,
          currentCount: 80,
          fileRecordCount: 30,
          importableCount: 20,
        })
      ).toBeTrue();
    });

    it('isPlanLimitWarning should return false for other errors', () => {
      expect(isPlanLimitWarning({ code: 'validation_error', message: 'bad' })).toBeFalse();
    });

    it('isPlanLimitWarning should return false for null', () => {
      expect(isPlanLimitWarning(null)).toBeFalse();
    });
  });
});

// ─── Component integration tests (with TestBed) ─────────────

describe('BulkImportComponent', () => {
  let component: BulkImportComponent;
  let fixture: ComponentFixture<BulkImportComponent>;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const baseUrl = `${environment.apiBaseUrl}/employees/import`;

  beforeEach(async () => {
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning']);

    await TestBed.configureTestingModule({
      imports: [BulkImportComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BulkImportComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    // Stub triggerBlobDownload to prevent real anchor.click() which kills Karma
    spyOn(component, 'triggerBlobDownload').and.callFake(() => {});
  });

  afterEach(() => {
    httpMock.verify();
    component.ngOnDestroy(); // clean up polling
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should start at step 0', () => {
    expect(component.currentStep()).toBe(0);
  });

  // ─── Step navigation ──────────────────────────────────────

  describe('step navigation', () => {
    it('should navigate to step 1 via goToStep(1)', () => {
      component.goToStep(1);
      expect(component.currentStep()).toBe(1);
    });

    it('should navigate back from step 1 to step 0', () => {
      component.goToStep(1);
      component.goToStep(0);
      expect(component.currentStep()).toBe(0);
    });
  });

  // ─── Template download ────────────────────────────────────

  describe('downloadTemplate', () => {
    it('should download CSV template via blob and call triggerBlobDownload', () => {
      component.downloadTemplate('csv');

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/template` && r.params.get('format') === 'csv'
      );
      expect(req.request.method).toBe('GET');
      req.flush(new Blob(['csv-data'], { type: 'text/csv' }));

      expect(component.triggerBlobDownload).toHaveBeenCalledTimes(1);
      const args = (component.triggerBlobDownload as jasmine.Spy).calls.mostRecent().args;
      expect(args[1]).toBe('employee-import-template.csv');
      expect(toastrSpy.success).toHaveBeenCalledTimes(1);
    });

    it('should download Excel template via blob', () => {
      component.downloadTemplate('xlsx');

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/template` && r.params.get('format') === 'xlsx'
      );
      req.flush(new Blob(['xlsx-data']));

      expect(component.triggerBlobDownload).toHaveBeenCalledTimes(1);
      const args = (component.triggerBlobDownload as jasmine.Spy).calls.mostRecent().args;
      expect(args[1]).toBe('employee-import-template.xlsx');
    });

    it('should show error toast on template download failure', () => {
      component.downloadTemplate('csv');

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/template`
      );
      req.error(new ProgressEvent('error'), { status: 500 });

      expect(toastrSpy.error).toHaveBeenCalledWith('Failed to download template.');
    });
  });

  // ─── File selection + validation ──────────────────────────

  describe('file selection', () => {
    it('should accept a valid .csv file', () => {
      const file = new File(['data'], 'employees.csv', { type: 'text/csv' });
      const event = { target: { files: [file], value: '' } } as unknown as Event;
      component.onFileSelected(event);

      expect(component.selectedFile()).toBe(file);
      expect(component.fileValidationError()).toBeNull();
    });

    it('should reject an invalid file type', () => {
      const file = new File(['data'], 'photo.jpg', { type: 'image/jpeg' });
      const event = { target: { files: [file], value: '' } } as unknown as Event;
      component.onFileSelected(event);

      expect(component.selectedFile()).toBeNull();
      expect(component.fileValidationError()).toBeTruthy();
    });

    it('should reject a file exceeding 25 MB', () => {
      const file = new File(['x'], 'big.csv', { type: 'text/csv' });
      Object.defineProperty(file, 'size', { value: MAX_IMPORT_FILE_SIZE_BYTES + 1 });
      const event = { target: { files: [file], value: '' } } as unknown as Event;
      component.onFileSelected(event);

      expect(component.selectedFile()).toBeNull();
      expect(component.fileValidationError()).toContain('25 MB');
    });

    it('should clear file and validation error on clearFile', () => {
      const file = new File(['data'], 'employees.csv', { type: 'text/csv' });
      const event = { target: { files: [file], value: '' } } as unknown as Event;
      component.onFileSelected(event);

      component.clearFile(new Event('click'));
      expect(component.selectedFile()).toBeNull();
      expect(component.fileValidationError()).toBeNull();
    });

    it('should handle drag and drop', () => {
      const file = new File(['data'], 'test.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      const dropEvent = {
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [file] },
      } as unknown as DragEvent;

      component.onDrop(dropEvent);
      expect(component.selectedFile()).toBe(file);
    });

    it('should set isDragOver on dragover and reset on dragleave', () => {
      const dragEvent = {
        preventDefault: () => {},
        stopPropagation: () => {},
      } as unknown as DragEvent;

      component.onDragOver(dragEvent);
      expect(component.isDragOver()).toBeTrue();

      component.onDragLeave(dragEvent);
      expect(component.isDragOver()).toBeFalse();
    });
  });

  // ─── Import submission ────────────────────────────────────

  describe('startImport', () => {
    it('should call service uploadImport and transition to results on sync response', () => {
      const file = new File(['csv-data'], 'employees.csv', { type: 'text/csv' });
      component.selectedFile.set(file);

      component.startImport();

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();

      // Simulate sync response
      const result: IImportResult = {
        total: 10, success: 10, failed: 0, errors: [],
      };
      req.event(new HttpResponse({ body: result }));

      expect(component.importResult()).toEqual(result);
      expect(component.currentStep()).toBe(2);
    });

    it('should not import when no file is selected', () => {
      component.startImport();
      httpMock.expectNone(baseUrl);
    });

    it('should handle plan-limit 409 response', () => {
      const file = new File(['csv-data'], 'employees.csv', { type: 'text/csv' });
      component.selectedFile.set(file);

      component.startImport();

      const req = httpMock.expectOne(baseUrl);
      req.flush(
        {
          code: 'plan_limit_exceeded',
          message: 'Would exceed limit',
          maxAllowed: 100,
          currentCount: 80,
          fileRecordCount: 30,
          importableCount: 20,
        },
        { status: 409, statusText: 'Conflict' }
      );

      expect(component.planLimitWarning()).toBeTruthy();
      expect(component.planLimitWarning()!.importableCount).toBe(20);
    });

    it('should show error toast on non-plan-limit error', () => {
      const file = new File(['csv-data'], 'employees.csv', { type: 'text/csv' });
      component.selectedFile.set(file);

      component.startImport();

      const req = httpMock.expectOne(baseUrl);
      req.error(new ProgressEvent('error'), { status: 500 });

      expect(toastrSpy.error).toHaveBeenCalledWith('Import failed. Please try again.');
    });
  });

  // ─── Plan-limit actions ───────────────────────────────────

  describe('plan limit actions', () => {
    it('should call importUpToLimit and clear warning', () => {
      const file = new File(['csv-data'], 'employees.csv', { type: 'text/csv' });
      component.selectedFile.set(file);
      component.planLimitWarning.set({
        code: 'plan_limit_exceeded',
        message: 'Limit',
        maxAllowed: 100,
        currentCount: 80,
        fileRecordCount: 30,
        importableCount: 20,
      });

      component.importUpToLimit();

      expect(component.planLimitWarning()).toBeNull();

      const req = httpMock.expectOne(baseUrl);
      const formData = req.request.body as FormData;
      expect(formData.get('importUpToLimit')).toBe('true');
      req.event(new HttpResponse({
        body: { total: 20, success: 20, failed: 0, errors: [] },
      }));

      expect(component.importResult()!.success).toBe(20);
    });

    it('should clear warning on cancel', () => {
      component.planLimitWarning.set({
        code: 'plan_limit_exceeded',
        message: 'Limit',
        maxAllowed: 100,
        currentCount: 80,
        fileRecordCount: 30,
        importableCount: 20,
      });
      component.cancelPlanLimit();
      expect(component.planLimitWarning()).toBeNull();
    });
  });

  // ─── Results rendering (computed signals) ─────────────────

  describe('results summary', () => {
    it('should compute all-success outcome', () => {
      component.importResult.set({ total: 50, success: 50, failed: 0, errors: [] });
      expect(component.outcome()).toBe('all-success');
      expect(component.summaryText()).toContain('50 of 50 records imported successfully');
    });

    it('should compute partial outcome', () => {
      component.importResult.set({
        total: 100, success: 95, failed: 5,
        errors: [{ row: 1, field: 'email', error: 'Duplicate' }],
      });
      expect(component.outcome()).toBe('partial');
      expect(component.summaryText()).toContain('95 of 100');
      expect(component.summaryText()).toContain('5 failed');
    });

    it('should compute all-failed outcome', () => {
      component.importResult.set({
        total: 10, success: 0, failed: 10,
        errors: [{ row: 1, field: 'email', error: 'Bad' }],
      });
      expect(component.outcome()).toBe('all-failed');
      expect(component.summaryText()).toContain('All 10 records failed');
    });

    it('should return null outcome when no result', () => {
      expect(component.outcome()).toBeNull();
      expect(component.summaryText()).toBe('');
    });
  });

  // ─── Error table rendering ────────────────────────────────

  describe('error table', () => {
    it('should render error rows in step 3', () => {
      component.importResult.set({
        total: 10, success: 8, failed: 2,
        errors: [
          { row: 3, field: 'email', error: 'Duplicate email' },
          { row: 7, field: 'department_name', error: 'Department not found' },
        ],
      });
      component.currentStep.set(2);
      fixture.detectChanges();

      const tableRows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(tableRows.length).toBe(2);
      expect(tableRows[0].textContent).toContain('3');
      expect(tableRows[0].textContent).toContain('email');
      expect(tableRows[0].textContent).toContain('Duplicate email');
    });

    it('should not render error table when no errors', () => {
      component.importResult.set({ total: 5, success: 5, failed: 0, errors: [] });
      component.currentStep.set(2);
      fixture.detectChanges();

      const errorSection = fixture.nativeElement.querySelector('tbody');
      expect(errorSection).toBeNull();
    });
  });

  // ─── Error report download ────────────────────────────────

  describe('downloadErrorReportCsv', () => {
    it('should generate client-side CSV when no jobId', () => {
      component.importResult.set({
        total: 5, success: 3, failed: 2,
        errors: [
          { row: 2, field: 'email', error: 'Invalid format' },
          { row: 4, field: 'department_name', error: 'Not found' },
        ],
      });

      component.downloadErrorReportCsv();

      expect(component.triggerBlobDownload).toHaveBeenCalledTimes(1);
      const args = (component.triggerBlobDownload as jasmine.Spy).calls.mostRecent().args;
      expect(args[1]).toBe('import-error-report.csv');
      // Verify the blob content is CSV
      expect(args[0] instanceof Blob).toBeTrue();
    });
  });

  // ─── Import another ───────────────────────────────────────

  describe('importAnother', () => {
    it('should reset state and go to step 1', () => {
      component.importResult.set({ total: 5, success: 5, failed: 0, errors: [] });
      component.currentStep.set(2);

      component.importAnother();

      expect(component.currentStep()).toBe(1);
      expect(component.importResult()).toBeNull();
      expect(component.selectedFile()).toBeNull();
    });
  });

  // ─── canImport computed ───────────────────────────────────

  describe('canImport', () => {
    it('should be false when no file selected', () => {
      expect(component.canImport()).toBeFalse();
    });

    it('should be true when file is selected with no errors', () => {
      const file = new File(['data'], 'test.csv', { type: 'text/csv' });
      component.selectedFile.set(file);
      expect(component.canImport()).toBeTrue();
    });

    it('should be false when uploading', () => {
      const file = new File(['data'], 'test.csv', { type: 'text/csv' });
      component.selectedFile.set(file);
      component.isUploading.set(true);
      expect(component.canImport()).toBeFalse();
    });

    it('should be false when there is a validation error', () => {
      const file = new File(['data'], 'test.csv', { type: 'text/csv' });
      component.selectedFile.set(file);
      component.fileValidationError.set('Too big');
      expect(component.canImport()).toBeFalse();
    });
  });

  // ─── formatFileSize ───────────────────────────────────────

  describe('formatFileSize', () => {
    it('should format bytes', () => {
      expect(component.formatFileSize(500)).toBe('500 B');
    });

    it('should format kilobytes', () => {
      expect(component.formatFileSize(2048)).toBe('2.0 KB');
    });

    it('should format megabytes', () => {
      expect(component.formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
    });
  });
});
