import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { AdjustmentService } from './adjustment.service';
import { environment } from '../../../../environments/environment';
import {
  IAdjustment,
  IAdjustmentRequest,
  IBulkAdjustmentResult,
} from '../models/adjustment.models';

describe('AdjustmentService', () => {
  let service: AdjustmentService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/payroll/adjustments`;

  const sample: IAdjustment = {
    id: 'a-1',
    employeeId: 'e-1',
    employeeName: 'Alex HR',
    employeeNo: 'EMP001',
    type: 'Bonus',
    amount: 10000,
    description: 'Q2 bonus',
    applicablePayMonth: 6,
    applicablePayYear: 2026,
    isTaxable: true,
    isRecurring: false,
    recurrenceEndMonth: null,
    recurrenceEndYear: null,
    status: 'Pending',
    hasDocument: false,
    createdAt: '2026-06-01T10:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AdjustmentService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(AdjustmentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listAdjustments GETs with no params and returns a bare array', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.params.keys().length).toBe(0);
    req.flush([sample]);

    expect(rows).toEqual([sample]);
  });

  it('listAdjustments reads items from the PayrollAdjustmentPageDto envelope', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock
      .expectOne(baseUrl)
      .flush({ items: [sample], totalCount: 1, page: 1, pageSize: 25 });
    expect(rows).toEqual([sample]);
  });

  it('listAdjustments tolerates a { data } page envelope', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock.expectOne(baseUrl).flush({ data: [sample] });
    expect(rows).toEqual([sample]);
  });

  it('listAdjustments defaults to [] for an unexpected payload', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock.expectOne(baseUrl).flush(null);
    expect(rows).toEqual([]);
  });

  it('listAdjustments serializes status/type/period/employeeId filters', () => {
    service
      .listAdjustments({
        status: 'Pending',
        type: 'Bonus',
        period: '2026-06',
        employeeId: 'e-1',
      })
      .subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('status') === 'Pending',
    );
    expect(req.request.params.get('type')).toBe('Bonus');
    expect(req.request.params.get('period')).toBe('2026-06');
    expect(req.request.params.get('employeeId')).toBe('e-1');
    req.flush([sample]);
  });

  it('listAdjustments omits empty/null filters', () => {
    service
      .listAdjustments({ status: null, type: null, period: '', employeeId: null })
      .subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.params.keys().length).toBe(0);
    req.flush([]);
  });

  it('createAdjustment POSTs the request body', () => {
    const request: IAdjustmentRequest = {
      employeeId: 'e-1',
      type: 'Bonus',
      amount: 10000,
      description: 'Q2 bonus',
      applicablePayMonth: 6,
      applicablePayYear: 2026,
      isTaxable: true,
      isRecurring: false,
      recurrenceEndMonth: null,
      recurrenceEndYear: null,
    };
    let created: IAdjustment | undefined;
    service.createAdjustment(request).subscribe((c) => (created = c));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    expect(req.request.withCredentials).toBeTrue();
    req.flush(sample);

    expect(created).toEqual(sample);
  });

  it('cancelAdjustment POSTs to the cancel endpoint (FR-6)', () => {
    let updated: IAdjustment | undefined;
    service.cancelAdjustment('a-1').subscribe((u) => (updated = u));

    const req = httpMock.expectOne(`${baseUrl}/a-1/cancel`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...sample, status: 'Cancelled' });

    expect(updated!.status).toBe('Cancelled');
  });

  it('bulkUpload POSTs multipart file+payMonth+payYear and returns the BE result (FR-2)', () => {
    const file = new File(['employee_no,adjustment_type,amount'], 'adj.csv', {
      type: 'text/csv',
    });
    const res: IBulkAdjustmentResult = {
      totalRows: 2,
      succeededCount: 1,
      failedCount: 1,
      results: [
        {
          rowNumber: 1,
          employeeNo: 'EMP001',
          success: true,
          adjustmentId: 'a-1',
          error: null,
          errorCode: null,
        },
        {
          rowNumber: 2,
          employeeNo: 'BAD',
          success: false,
          adjustmentId: null,
          error: 'Unknown employee',
          errorCode: 'EMP_NOT_FOUND',
        },
      ],
    };
    let result: IBulkAdjustmentResult | undefined;
    service.bulkUpload(file, 6, 2026).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/bulk`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    const body = req.request.body as FormData;
    expect(body.get('file')).toBeInstanceOf(File);
    expect(body.get('payMonth')).toBe('6');
    expect(body.get('payYear')).toBe('2026');
    req.flush(res);

    expect(result).toEqual(res);
  });

  it('bulkTemplateCsv includes an example data row under the header', () => {
    const lines = service.bulkTemplateCsv().split('\n');
    expect(lines.length).toBeGreaterThan(1);
    // The example row carries a valid adjustment_type from the allowed set.
    expect(lines[1]).toContain('Bonus');
  });

  it('uploadDocument POSTs multipart to the document endpoint (AC-3)', () => {
    const file = new File(['%PDF'], 'receipt.pdf', { type: 'application/pdf' });
    service.uploadDocument('a-1', file).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/a-1/document`);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect(body.get('file')).toBeInstanceOf(File);
    req.flush({ ...sample, hasDocument: true });
  });

  it('downloadDocument GETs a blob with the full response (AC-3)', () => {
    let resp: { body: Blob | null } | undefined;
    service.downloadDocument('a-1').subscribe((r) => (resp = r));

    const req = httpMock.expectOne(`${baseUrl}/a-1/document`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.7'], { type: 'application/pdf' }));

    expect(resp!.body).toBeInstanceOf(Blob);
  });

  it('bulkTemplateCsv builds the CSV template client-side with all 5 columns (§8)', () => {
    const csv = service.bulkTemplateCsv();
    const header = csv.split('\n')[0];
    expect(header).toBe(
      'employee_no,adjustment_type,amount,description,is_taxable',
    );
    // Pure client-side: no HTTP request is made. httpMock.verify() (afterEach) asserts this.
    expect(csv.split('\n').length).toBeGreaterThan(1);
  });
});
