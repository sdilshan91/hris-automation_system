import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayslipService } from './payslip.service';
import { environment } from '../../../../environments/environment';
import {
  IPayslip,
  IPayslipGenerationStatus,
  PayslipGenerationAcceptedWire,
  PayslipGenerationStatusWire,
  PayslipWire,
} from '../models/payslip.models';

describe('PayslipService', () => {
  let service: PayslipService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  // The WIRE shape — exactly what `PayrollPayslipListItemDto` puts on the socket. Note `slipId`,
  // NOT `id`: the old spec flushed the view-model shape, which is why the rename stayed green.
  const sampleWireRows: PayslipWire[] = [
    {
      slipId: 's-1',
      employeeId: 'e-1',
      employeeName: 'Alex HR',
      employeeNo: 'EMP001',
      department: 'People',
      netSalary: 5000,
      pdfStatus: 'Generated',
      pdfGeneratedAt: '2026-05-31T10:05:00Z',
      pdfFileSizeBytes: 20480,
    },
  ];

  /** What the mapper must produce from `sampleWireRows` — what the §8 table binds to. */
  const sampleRows: IPayslip[] = [
    {
      id: 's-1',
      employeeId: 'e-1',
      employeeName: 'Alex HR',
      employeeNo: 'EMP001',
      department: 'People',
      netSalary: 5000,
      pdfStatus: 'Generated',
      pdfGeneratedAt: '2026-05-31T10:05:00Z',
    },
  ];

  // The WIRE shape of the poll snapshot. EVERY name differs from the view-model, and `isComplete`
  // is the INVERSE of `isGenerating` — the old spec flushed `isGenerating`, a field the server has
  // never sent, plus `generatedCount`, a field that exists on neither side.
  const settledStatusWire: PayslipGenerationStatusWire = {
    runId: 'r-1',
    isComplete: true,
    totalSlips: 250,
    generated: 250,
    failed: 0,
    pending: 0,
  };

  const settledStatus: IPayslipGenerationStatus = {
    runId: 'r-1',
    isGenerating: false,
    totalCount: 250,
    queuedCount: 250,
    failedCount: 0,
    pendingCount: 0,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayslipService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayslipService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listPayslips GETs the run payslips and returns a bare array', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(sampleWireRows);

    expect(rows).toEqual(sampleRows);
  });

  it('listPayslips tolerates a { data } page envelope', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips`)
      .flush({ data: sampleWireRows });
    expect(rows).toEqual(sampleRows);
  });

  it('listPayslips defaults to [] for an unexpected payload', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock.expectOne(`${runsUrl}/r-1/payslips`).flush(null);
    expect(rows).toEqual([]);
  });

  it('generatePayslips POSTs to the generate endpoint', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.generatePayslips('r-1').subscribe((s) => (status = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/generate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    // WIRE: the 202 body is `PayrollPayslipGenerationAcceptedDto` — no completion flag, no totals.
    const accepted: PayslipGenerationAcceptedWire = {
      runId: 'r-1',
      queuedCount: 500,
      regenerated: false,
    };
    req.flush(accepted, { status: 202, statusText: 'Accepted' });

    expect(status!.isGenerating).toBeTrue();
  });

  it('generatePayslips reports the enqueued slips as PENDING, never as generated', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.generatePayslips('r-1').subscribe((s) => (status = s));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/generate`)
      .flush({ runId: 'r-1', queuedCount: 500, regenerated: false });

    // §8 renders `{{ queuedCount }} / {{ totalCount }} generated`. Mapping the wire's enqueued
    // count straight onto the view-model's (misnamed) `queuedCount` would claim "500 / 500
    // generated" the instant the button was clicked.
    expect(status!.queuedCount).toBe(0);
    expect(status!.totalCount).toBe(500);
    expect(status!.pendingCount).toBe(500);
    expect(status!.failedCount).toBe(0);
    expect(status!.runId).toBe('r-1');
  });

  it('generatePayslips stays "generating" even when the 202 body is empty', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.generatePayslips('r-1').subscribe((s) => (status = s));

    httpMock.expectOne(`${runsUrl}/r-1/payslips/generate`).flush({});

    // A 202 means work was accepted; nothing here may report completion.
    expect(status!.isGenerating).toBeTrue();
    expect(status!.totalCount).toBe(0);
  });

  it('regeneratePayslips POSTs to the regenerate endpoint (AC-5)', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.regeneratePayslips('r-1').subscribe((s) => (status = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/regenerate`);
    expect(req.request.method).toBe('POST');
    req.flush({ runId: 'r-1', queuedCount: 250, regenerated: true });

    expect(status!.isGenerating).toBeTrue();
    expect(status!.totalCount).toBe(250);
  });

  it('retryPayslip POSTs to the per-employee retry endpoint (DF-31, FR-8)', () => {
    let done = false;
    service.retryPayslip('r-1', 'e-3').subscribe(() => (done = true));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/e-3/retry`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(null, { status: 202, statusText: 'Accepted' });

    expect(done).toBeTrue();
  });

  it('getGenerationStatus GETs the status endpoint and renames every field', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.getGenerationStatus('r-1').subscribe((s) => (status = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/status`);
    expect(req.request.method).toBe('GET');
    req.flush(settledStatusWire);

    // totalSlips → totalCount, generated → queuedCount, failed → failedCount,
    // pending → pendingCount, and isComplete → !isGenerating.
    expect(status).toEqual(settledStatus);
    expect(status as unknown).not.toBe(settledStatusWire);
  });

  it('getGenerationStatus INVERTS isComplete into isGenerating', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.getGenerationStatus('r-1').subscribe((s) => (status = s));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/status`)
      .flush({ ...settledStatusWire, isComplete: false, generated: 100, pending: 150 });

    expect(status!.isGenerating).toBeTrue();
    expect(status!.queuedCount).toBe(100);
    expect(status!.pendingCount).toBe(150);
  });

  it('getGenerationStatus treats an ABSENT isComplete as STILL RUNNING', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.getGenerationStatus('r-1').subscribe((s) => (status = s));

    // `isComplete` is REQUIRED in the schema, so this is a malformed/degraded payload rather than a
    // routine one — the point is which way the mapper falls when the flag simply is not there.
    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/status`)
      .flush({ runId: 'r-1', totalSlips: 250, generated: 250, failed: 0, pending: 0 });

    // The least-claiming default: `isGenerating === false` ends the poll and makes the component
    // toast "Payslip generation finished". A missing flag must never trigger that.
    expect(status!.isGenerating).toBeTrue();
  });

  it('getGenerationStatus does NOT coerce an absent pdfStatus on list rows', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips`)
      .flush([{ ...sampleWireRows[0], pdfStatus: null, pdfGeneratedAt: null }]);

    // Coercing to `Generated` would claim a PDF exists; coercing to `Failed` would put a red badge
    // and a Retry button on a healthy slip. Neither is guessed.
    expect(rows![0].pdfStatus as string).toBe('');
    expect(rows![0].pdfGeneratedAt).toBeNull();
  });

  it('listPayslips maps slipId → id (the per-row action key)', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock.expectOne(`${runsUrl}/r-1/payslips`).flush(sampleWireRows);

    // The wire field is `slipId`; download/preview/retry are all keyed off `id`, which was
    // permanently `undefined` before the mapper.
    expect(rows![0].id).toBe('s-1');
    expect(rows![0].department).toBe('People');
  });

  it('listPayslips keeps a null department null (not an empty string)', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips`)
      .flush([{ ...sampleWireRows[0], department: null }]);

    expect(rows![0].department).toBeNull();
  });

  it('downloadPayslip GETs a blob with the response so the filename is readable', () => {
    let resp: { body: Blob | null } | undefined;
    service.downloadPayslip('r-1', 'e-1').subscribe((r) => (resp = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/e-1/download`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.7'], { type: 'application/pdf' }));

    expect(resp!.body).toBeInstanceOf(Blob);
  });

  it('downloadZip GETs the bulk ZIP as a blob (AC-3)', () => {
    let resp: { body: Blob | null } | undefined;
    service.downloadZip('r-1').subscribe((r) => (resp = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/download-zip`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['PK'], { type: 'application/zip' }));

    expect(resp!.body).toBeInstanceOf(Blob);
  });

  describe('streamGenerationStatus (polling, §8)', () => {
    it('polls while generating and completes on the first settled snapshot', fakeAsync(() => {
      const emitted: IPayslipGenerationStatus[] = [];
      let completed = false;
      service.streamGenerationStatus('r-1').subscribe({
        next: (s) => emitted.push(s),
        complete: () => (completed = true),
      });

      // timer(0, …) does NOT fire synchronously on subscribe — tick(0) first.
      tick(0);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ ...settledStatusWire, isComplete: false, generated: 100 });
      expect(emitted.length).toBe(1);
      expect(completed).toBeFalse();

      tick(PayslipService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ ...settledStatusWire, isComplete: true, generated: 250 });

      // Settled snapshot is emitted (inclusive takeWhile) then the stream completes.
      expect(emitted.length).toBe(2);
      expect(completed).toBeTrue();
      expect(emitted[1].queuedCount).toBe(250);

      discardPeriodicTasks();
    }));

    it('keeps polling on the REAL wire shape (no isGenerating field on the socket)', fakeAsync(() => {
      const emitted: IPayslipGenerationStatus[] = [];
      let completed = false;
      service.streamGenerationStatus('r-1').subscribe({
        next: (s) => emitted.push(s),
        complete: () => (completed = true),
      });

      // This is the regression: the server sends `isComplete`, never `isGenerating`. Before the
      // mapper, `s.isGenerating` was `undefined` (falsy), so `takeWhile(…, true)` completed after
      // ONE emission and the component toasted "Payslip generation finished" immediately.
      tick(0);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ runId: 'r-1', totalSlips: 250, generated: 10, pending: 240, failed: 0, isComplete: false });
      expect(completed).toBeFalse();

      tick(PayslipService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ runId: 'r-1', totalSlips: 250, generated: 120, pending: 130, failed: 0, isComplete: false });
      expect(completed).toBeFalse();
      expect(emitted.length).toBe(2);

      tick(PayslipService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ runId: 'r-1', totalSlips: 250, generated: 248, pending: 0, failed: 2, isComplete: true });
      expect(completed).toBeTrue();
      expect(emitted.length).toBe(3);
      expect(emitted[2].failedCount).toBe(2);

      discardPeriodicTasks();
    }));
  });
});
