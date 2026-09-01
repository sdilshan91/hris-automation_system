import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { AdjustmentService } from './adjustment.service';
import { environment } from '../../../../environments/environment';
import {
  AdjustmentWire,
  BulkAdjustmentResultWire,
  CreateAdjustmentResultWire,
  IAdjustment,
  IAdjustmentRequest,
  IBulkAdjustmentResult,
} from '../models/adjustment.models';

describe('AdjustmentService', () => {
  let service: AdjustmentService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/payroll/adjustments`;

  // The WIRE shape — exactly what `PayrollPayrollAdjustmentDto` puts on the socket. Note
  // `hasSupportingDocument`, NOT `hasDocument`: the old spec flushed the view-model shape, which is
  // why the rename bug stayed green.
  const sampleWire: AdjustmentWire = {
    id: 'a-1',
    employeeId: 'e-1',
    employeeName: 'Alex HR',
    employeeNo: 'EMP001',
    adjustmentType: 'Bonus',
    amount: 10000,
    description: 'Q2 bonus',
    applicablePayMonth: 6,
    applicablePayYear: 2026,
    isTaxable: true,
    isRecurring: false,
    recurrenceEndMonth: null,
    recurrenceEndYear: null,
    status: 'Pending',
    hasSupportingDocument: false,
    createdAt: '2026-06-01T10:00:00Z',
  };

  /** What the mapper must produce from `sampleWire` — what components actually bind to. */
  const sample: IAdjustment = {
    id: 'a-1',
    employeeId: 'e-1',
    employeeName: 'Alex HR',
    employeeNo: 'EMP001',
    adjustmentType: 'Bonus',
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
    req.flush([sampleWire]);

    expect(rows).toEqual([sample]);
  });

  it('listAdjustments reads items from the PayrollAdjustmentPageDto envelope', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock
      .expectOne(baseUrl)
      .flush({ items: [sampleWire], totalCount: 1, page: 1, pageSize: 25 });
    expect(rows).toEqual([sample]);
  });

  it('listAdjustments tolerates a { data } page envelope', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock.expectOne(baseUrl).flush({ data: [sampleWire] });
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
    req.flush([sampleWire]);
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
      adjustmentType: 'Bonus',
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
    // WIRE: the create endpoint answers with `PayrollCreatePayrollAdjustmentResult`, a WRAPPER —
    // the record lives under `adjustment`, not at the root.
    const wrapper: CreateAdjustmentResultWire = {
      adjustment: sampleWire,
      deferredToPayMonth: null,
      deferredToPayYear: null,
      generatedOccurrences: 1,
      negativeNetWarning: false,
    };
    req.flush(wrapper);

    // The old spec flushed the record at the ROOT, so `created.id` being `undefined` in production
    // never showed up here. Asserting the id specifically is the regression guard.
    expect(created).toEqual(sample);
    expect(created!.id).toBe('a-1');
  });

  it('createAdjustment maps an EMPTY wrapper without throwing (no adjustment key)', () => {
    const request: IAdjustmentRequest = {
      employeeId: 'e-1',
      adjustmentType: 'Bonus',
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
    httpMock.expectOne(baseUrl).flush({ generatedOccurrences: 0 });

    expect(created!.id).toBe('');
    // Least-claiming defaults: no taxability, no recurrence, no evidence of a document.
    expect(created!.isTaxable).toBeFalse();
    expect(created!.hasDocument).toBeFalse();
  });

  // NOT migrated to a wire type: `POST /adjustments/{id}/cancel` answers with a bare `ApiResponse`
  // (no `data`), so the service's `Observable<IAdjustment>` has no wire source and the caller's
  // `updated.id` is `undefined` in production. Reported as a finding — fixing it needs a component
  // change, which is outside this task's lane. The assertions below are left exactly as they were.
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
    // Wire and view-model happen to share field names here, but flush the WIRE type so a future
    // backend rename breaks this spec instead of production.
    const wire: BulkAdjustmentResultWire = res;
    req.flush(wire);

    expect(result).toEqual(res);
    // The service now MAPS, so it must be a new object rather than the flushed body.
    expect(result as unknown).not.toBe(wire as unknown);
  });

  it('bulkUpload defaults an absent per-row `success` to FALSE (never claims an import)', () => {
    const file = new File(['x'], 'adj.csv', { type: 'text/csv' });
    let result: IBulkAdjustmentResult | undefined;
    service.bulkUpload(file, 6, 2026).subscribe((r) => (result = r));

    // A row payload with NO `success` flag and NO counts.
    httpMock
      .expectOne(`${baseUrl}/bulk`)
      .flush({ results: [{ rowNumber: 1, employeeNo: 'EMP001' }] });

    expect(result!.results[0].success).toBeFalse();
    expect(result!.results[0].adjustmentId).toBeNull();
    // Server-computed counts fall back to 0 (see the defaulting policy in adjustment.models.ts).
    expect(result!.totalRows).toBe(0);
    expect(result!.succeededCount).toBe(0);
    expect(result!.failedCount).toBe(0);
  });

  it('bulkTemplateCsv includes an example data row under the header', () => {
    const lines = service.bulkTemplateCsv().split('\n');
    expect(lines.length).toBeGreaterThan(1);
    // The example row carries a valid adjustment_type from the allowed set.
    expect(lines[1]).toContain('Bonus');
  });

  // NOT migrated: this endpoint answers with `ApiResponseOfString` (the stored path), not the
  // adjustment. Reported as a finding; the assertions below are unchanged.
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

  it('listAdjustments maps hasSupportingDocument → hasDocument (AC-3 rename)', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock
      .expectOne(baseUrl)
      .flush([{ ...sampleWire, hasSupportingDocument: true }]);

    // The wire name is `hasSupportingDocument`; the §8 template binds `hasDocument`. Without the
    // mapper this was permanently `undefined`, so the download control never rendered.
    expect(rows![0].hasDocument).toBeTrue();
  });

  it('listAdjustments defaults an ABSENT hasSupportingDocument to false (no phantom evidence)', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    const noDocFlag: AdjustmentWire = { ...sampleWire };
    delete noDocFlag.hasSupportingDocument;
    httpMock.expectOne(baseUrl).flush([noDocFlag]);

    expect(rows![0].hasDocument).toBeFalse();
  });

  it('listAdjustments does NOT coerce an absent type/status into a meaningful state', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    httpMock
      .expectOne(baseUrl)
      .flush([{ ...sampleWire, adjustmentType: null, status: null }]);

    // Defaulting the TYPE would flip money between an earning and a deduction; defaulting the
    // STATUS would claim the money moved (Applied) or did not (Cancelled). Neither is guessed.
    expect(rows![0].adjustmentType as string).toBe('');
    expect(rows![0].status as string).toBe('');
  });

  it('listAdjustments maps rather than passing the flushed body through', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    const body = [sampleWire];
    httpMock.expectOne(baseUrl).flush(body);

    expect(rows).toEqual([sample]);
    expect(rows![0] as unknown).not.toBe(sampleWire);
  });

  it('listAdjustments keeps recurrence-end nulls null (not 0)', () => {
    let rows: IAdjustment[] | undefined;
    service.listAdjustments().subscribe((r) => (rows = r));

    const noRecurrence: AdjustmentWire = { ...sampleWire };
    delete noRecurrence.recurrenceEndMonth;
    delete noRecurrence.recurrenceEndYear;
    httpMock.expectOne(baseUrl).flush([noRecurrence]);

    // The UI renders "not recurring" differently from "ends in month 0".
    expect(rows![0].recurrenceEndMonth).toBeNull();
    expect(rows![0].recurrenceEndYear).toBeNull();
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
