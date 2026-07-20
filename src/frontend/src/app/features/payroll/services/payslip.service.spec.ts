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
} from '../models/payslip.models';

describe('PayslipService', () => {
  let service: PayslipService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

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

  const sampleStatus: IPayslipGenerationStatus = {
    runId: 'r-1',
    isGenerating: false,
    totalCount: 250,
    generatedCount: 250,
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
    req.flush(sampleRows);

    expect(rows).toEqual(sampleRows);
  });

  it('listPayslips tolerates a { data } page envelope', () => {
    let rows: IPayslip[] | undefined;
    service.listPayslips('r-1').subscribe((r) => (rows = r));

    httpMock.expectOne(`${runsUrl}/r-1/payslips`).flush({ data: sampleRows });
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
    req.flush({ ...sampleStatus, isGenerating: true, generatedCount: 0 });

    expect(status!.isGenerating).toBeTrue();
  });

  it('regeneratePayslips POSTs to the regenerate endpoint (AC-5)', () => {
    service.regeneratePayslips('r-1').subscribe();
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/regenerate`);
    expect(req.request.method).toBe('POST');
    req.flush(sampleStatus);
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

  it('getGenerationStatus GETs the status endpoint', () => {
    let status: IPayslipGenerationStatus | undefined;
    service.getGenerationStatus('r-1').subscribe((s) => (status = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/status`);
    expect(req.request.method).toBe('GET');
    req.flush(sampleStatus);

    expect(status).toEqual(sampleStatus);
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
        .flush({ ...sampleStatus, isGenerating: true, generatedCount: 100 });
      expect(emitted.length).toBe(1);
      expect(completed).toBeFalse();

      tick(PayslipService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/status`)
        .flush({ ...sampleStatus, isGenerating: false, generatedCount: 250 });

      // Settled snapshot is emitted (inclusive takeWhile) then the stream completes.
      expect(emitted.length).toBe(2);
      expect(completed).toBeTrue();

      discardPeriodicTasks();
    }));
  });
});
