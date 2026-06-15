import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayrollRunService } from './payroll-run.service';
import { environment } from '../../../../environments/environment';
import {
  IPayrollRun,
  IPayrollRunProgress,
  IPayrollRunValidation,
} from '../models/payroll-run.models';

describe('PayrollRunService', () => {
  let service: PayrollRunService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  const mockRun: IPayrollRun = {
    id: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'ReviewPending',
    totalEmployees: 250,
    processedEmployees: 247,
    skippedEmployees: 3,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalNet: 800000,
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayrollRunService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayrollRunService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── listRuns ─────────────────────────────────────────────

  describe('listRuns', () => {
    it('GETs the runs url and returns a bare array', () => {
      let result: IPayrollRun[] | undefined;
      service.listRuns().subscribe((r) => (result = r));

      const req = httpMock.expectOne(runsUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockRun]);

      expect(result).toEqual([mockRun]);
    });

    it('tolerates a { data } envelope', () => {
      let result: IPayrollRun[] | undefined;
      service.listRuns().subscribe((r) => (result = r));

      httpMock.expectOne(runsUrl).flush({ data: [mockRun] });
      expect(result).toEqual([mockRun]);
    });

    it('defaults to [] for an unexpected shape', () => {
      let result: IPayrollRun[] | undefined;
      service.listRuns().subscribe((r) => (result = r));

      httpMock.expectOne(runsUrl).flush(null);
      expect(result).toEqual([]);
    });
  });

  // ─── getRun ───────────────────────────────────────────────

  describe('getRun', () => {
    it('GETs the run id url', () => {
      let result: IPayrollRun | undefined;
      service.getRun('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/r-1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockRun);

      expect(result).toEqual(mockRun);
    });
  });

  // ─── validateRun ──────────────────────────────────────────

  describe('validateRun', () => {
    it('POSTs the period to /validate and returns the summary', () => {
      const summary: IPayrollRunValidation = {
        totalEmployees: 250,
        readyEmployees: 247,
        missingSalaryStructure: 3,
        canRun: true,
        blockers: [],
      };
      let result: IPayrollRunValidation | undefined;
      service
        .validateRun({ payMonth: 5, payYear: 2026 })
        .subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/validate`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ payMonth: 5, payYear: 2026 });
      req.flush(summary);

      expect(result).toEqual(summary);
    });
  });

  // ─── initiateRun ──────────────────────────────────────────

  describe('initiateRun', () => {
    it('POSTs the period with an Idempotency-Key header (FR-9)', () => {
      let result: IPayrollRun | undefined;
      service
        .initiateRun({ payMonth: 5, payYear: 2026 }, 'idem-123')
        .subscribe((r) => (result = r));

      const req = httpMock.expectOne(runsUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ payMonth: 5, payYear: 2026 });
      expect(req.request.headers.get('Idempotency-Key')).toBe('idem-123');
      req.flush({ ...mockRun, status: 'Queued' });

      expect(result?.status).toBe('Queued');
    });
  });

  // ─── getProgress ──────────────────────────────────────────

  describe('getProgress', () => {
    it('GETs the run progress url', () => {
      const progress: IPayrollRunProgress = {
        runId: 'r-1',
        status: 'Processing',
        processedEmployees: 100,
        totalEmployees: 250,
        skippedEmployees: 0,
      };
      let result: IPayrollRunProgress | undefined;
      service.getProgress('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/r-1/progress`);
      expect(req.request.method).toBe('GET');
      req.flush(progress);

      expect(result).toEqual(progress);
    });
  });

  // ─── streamProgress (polling, FR-6) ───────────────────────

  describe('streamProgress', () => {
    function progress(
      status: IPayrollRunProgress['status'],
      processed: number,
    ): IPayrollRunProgress {
      return {
        runId: 'r-1',
        status,
        processedEmployees: processed,
        totalEmployees: 250,
        skippedEmployees: 0,
      };
    }

    it('polls every interval while Processing and completes on a terminal status', fakeAsync(() => {
      const emissions: IPayrollRunProgress[] = [];
      let completed = false;
      service.streamProgress('r-1').subscribe({
        next: (p) => emissions.push(p),
        complete: () => (completed = true),
      });

      // First poll fires on the timer's 0ms tick (flushed by tick(0)).
      tick(0);
      httpMock.expectOne(`${runsUrl}/r-1/progress`).flush(progress('Processing', 50));
      expect(emissions.length).toBe(1);
      expect(completed).toBeFalse();

      // Next poll after one interval — still processing.
      tick(PayrollRunService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/progress`)
        .flush(progress('Processing', 150));
      expect(emissions.length).toBe(2);
      expect(completed).toBeFalse();

      // Next poll — terminal status: emit it, then complete (takeWhile inclusive).
      tick(PayrollRunService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/progress`)
        .flush(progress('ReviewPending', 250));
      expect(emissions.length).toBe(3);
      expect(emissions[2].status).toBe('ReviewPending');
      expect(completed).toBeTrue();

      // No further polls after completion.
      tick(PayrollRunService.POLL_INTERVAL_MS);
      httpMock.expectNone(`${runsUrl}/r-1/progress`);
    }));
  });
});
