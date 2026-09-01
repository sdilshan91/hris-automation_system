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
  RUN_STATUS_LABELS,
  canGeneratePayslipsFor,
  PayrollRunAcceptedWire,
  PayrollRunProgressWire,
  PayrollRunWire,
} from '../models/payroll-run.models';

describe('PayrollRunService', () => {
  let service: PayrollRunService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  /**
   * D1 wire-types slice: the flushed body is now the WIRE shape (`PayrollRunDto`), not the
   * view-model. The previous mock flushed `initiatedByName`, which this DTO has NEVER carried —
   * the server sends `initiatedBy` (a uuid) — so the old assertions were green against a payload
   * the backend cannot produce.
   */
  const wireRun: PayrollRunWire = {
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
    totalStatutory: 120000,
    initiatedBy: 'u-9',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
    approvedAt: null,
    approvedBy: null,
    finalizedAt: null,
  };

  /** What `mapPayrollRun(wireRun)` must produce. */
  const mappedRun: IPayrollRun = {
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
    // The DTO has no name field — the mapper must NOT invent one from `initiatedBy`.
    initiatedByName: null,
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
  };

  /** The 202/200 accepted DTO the initiate / cancel / rerun endpoints actually return. */
  const wireAccepted: PayrollRunAcceptedWire = {
    runId: 'r-1',
    status: 'Queued',
    payMonth: 5,
    payYear: 2026,
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
      req.flush([wireRun]);

      expect(result).toEqual([mappedRun]);
      // The mapper must produce a NEW object, not pass the wire payload through.
      expect(result![0]).not.toBe(wireRun as unknown as IPayrollRun);
    });

    it('tolerates a { data } envelope', () => {
      let result: IPayrollRun[] | undefined;
      service.listRuns().subscribe((r) => (result = r));

      httpMock.expectOne(runsUrl).flush({ data: [wireRun] });
      expect(result).toEqual([mappedRun]);
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
      req.flush(wireRun);

      expect(result).toEqual(mappedRun);
      expect(result).not.toBe(wireRun as unknown as IPayrollRun);
    });

    it('defaults every absent wire field without claiming a lifecycle state', () => {
      let result: IPayrollRun | undefined;
      service.getRun('r-2').subscribe((r) => (result = r));

      // A payload with only an id — every other property omitted by the server.
      httpMock.expectOne(`${runsUrl}/r-2`).flush({ id: 'r-2' });

      // An absent status must NOT become Queued/Approved/Finalized — it becomes the
      // backend's own Unknown sentinel, which gates nothing.
      expect(result!.status as string).toBe('Unknown');
      expect(canGeneratePayslipsFor(result!.status)).toBeFalse();
      expect(RUN_STATUS_LABELS[result!.status]).toBe('Unknown');
      // Server-computed aggregates default to 0; display-only fields default to null.
      expect(result!.totalNet).toBe(0);
      expect(result!.totalGross).toBe(0);
      expect(result!.totalEmployees).toBe(0);
      expect(result!.initiatedByName).toBeNull();
      expect(result!.completedAt).toBeNull();
    });

    it('does not coerce an unrecognised status string into a known state', () => {
      let result: IPayrollRun | undefined;
      service.getRun('r-3').subscribe((r) => (result = r));

      httpMock
        .expectOne(`${runsUrl}/r-3`)
        .flush({ ...wireRun, id: 'r-3', status: 'SomethingNew' });

      expect(result!.status as string).toBe('Unknown');
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
      // 202 Accepted returns PayrollRunAcceptedDto — NOT a full run.
      req.flush(wireAccepted);

      expect(result?.status).toBe('Queued');
    });

    it('maps the accepted DTO `runId` onto `id` (the run-created navigation target)', () => {
      let result: IPayrollRun | undefined;
      service
        .initiateRun({ payMonth: 5, payYear: 2026 }, 'idem-124')
        .subscribe((r) => (result = r));

      httpMock.expectOne(runsUrl).flush(wireAccepted);

      // Regression: the response has no `id`, so an unmapped payload navigated to
      // /payroll/runs/undefined after starting a run.
      expect(result!.id).toBe('r-1');
      expect(result!.payMonth).toBe(5);
      expect(result!.payYear).toBe(2026);
    });
  });

  // ─── cancelRun / rerunRun (ISSUE-154) ─────────────────────

  describe('cancelRun', () => {
    it('POSTs to /{runId}/cancel and returns the accepted run', () => {
      let result: IPayrollRun | undefined;
      service.cancelRun('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/r-1/cancel`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...wireAccepted, status: 'Cancelled' });

      expect(result?.status).toBe('Cancelled');
      expect(result?.id).toBe('r-1');
    });
  });

  describe('rerunRun', () => {
    it('POSTs to /{runId}/rerun', () => {
      let result: IPayrollRun | undefined;
      service.rerunRun('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/r-1/rerun`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(wireAccepted);

      expect(result?.status).toBe('Queued');
      expect(result?.id).toBe('r-1');
    });
  });

  // ─── getProgress ──────────────────────────────────────────

  describe('getProgress', () => {
    it('GETs the run progress url and maps the wire DTO', () => {
      // The wire DTO also carries `isComplete`, which the view-model deliberately drops.
      const wireProgress: PayrollRunProgressWire = {
        runId: 'r-1',
        status: 'Processing',
        processedEmployees: 100,
        totalEmployees: 250,
        skippedEmployees: 0,
        isComplete: false,
      };
      let result: IPayrollRunProgress | undefined;
      service.getProgress('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${runsUrl}/r-1/progress`);
      expect(req.request.method).toBe('GET');
      req.flush(wireProgress);

      expect(result).toEqual({
        runId: 'r-1',
        status: 'Processing',
        processedEmployees: 100,
        totalEmployees: 250,
        skippedEmployees: 0,
      });
      expect(result).not.toBe(wireProgress as unknown as IPayrollRunProgress);
    });

    it('maps an absent progress status to Unknown, which stops the poll loop', () => {
      let result: IPayrollRunProgress | undefined;
      service.getProgress('r-1').subscribe((r) => (result = r));

      httpMock.expectOne(`${runsUrl}/r-1/progress`).flush({ runId: 'r-1' });

      expect(result!.status as string).toBe('Unknown');
      expect(result!.processedEmployees).toBe(0);
      expect(result!.totalEmployees).toBe(0);
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
