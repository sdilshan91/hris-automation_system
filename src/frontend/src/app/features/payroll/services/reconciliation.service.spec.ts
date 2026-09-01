import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { ReconciliationService } from './reconciliation.service';
import { environment } from '../../../../environments/environment';
import {
  ILeaveEncashmentRequest,
  ILeaveEncashmentResult,
  IReconciliationReport,
  LeaveEncashmentResultWire,
  ReconciliationReportWire,
} from '../models/reconciliation.models';

describe('ReconciliationService', () => {
  let service: ReconciliationService;
  let httpMock: HttpTestingController;
  const payrollUrl = `${environment.apiBaseUrl}/payroll`;
  const reconciliationUrl = `${payrollUrl}/reconciliation`;
  const encashmentUrl = `${payrollUrl}/leave-encashments`;

  // WIRE shapes — exactly what the API emits (PayrollPrePayrollReconciliationDto /
  // PayrollLeaveEncashmentResultDto). Flushing a view-model here would have hidden any
  // rename, which is the drift class this migration exists to catch.
  const mockReport: ReconciliationReportWire = {
    period: '2026-05',
    payMonth: 5,
    payYear: 2026,
    attendanceFinalized: true,
    rows: [
      {
        employeeId: 'e-1',
        employeeNo: 'EMP-001',
        employeeName: 'Alex Doe',
        workingDays: 22,
        presentDays: 20,
        absentDays: 2,
        leaveDaysByType: { Annual: 2 },
        totalLeaveDays: 2,
        overtimeHours: 4,
        calculatedLopDays: 0,
      },
    ],
  };

  const mockResult: LeaveEncashmentResultWire = {
    adjustmentId: 'adj-1',
    employeeId: 'e-1',
    encashedDays: 5,
    dailyRate: 100,
    amount: 500,
    payMonth: 6,
    payYear: 2026,
    periodDeferred: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ReconciliationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ReconciliationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getReconciliation (FR-7) ─────────────────────────────

  describe('getReconciliation', () => {
    it('GETs the reconciliation url with payYear + payMonth params', () => {
      let result: IReconciliationReport | undefined;
      service.getReconciliation(2026, 5).subscribe((r) => (result = r));

      const req = httpMock.expectOne(
        (r) => r.url === reconciliationUrl,
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('payYear')).toBe('2026');
      expect(req.request.params.get('payMonth')).toBe('5');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockReport);

      expect(result).toEqual(mockReport as IReconciliationReport);
    });

    it('unwraps a { data } envelope (US-PLT-001 defensive)', () => {
      let result: IReconciliationReport | undefined;
      service.getReconciliation(2026, 5).subscribe((r) => (result = r));

      const req = httpMock.expectOne((r) => r.url === reconciliationUrl);
      req.flush({ data: mockReport });

      expect(result).toEqual(mockReport as IReconciliationReport);
    });

    it('maps a bare payload (no envelope) field-for-field', () => {
      let result: IReconciliationReport | undefined;
      service.getReconciliation(2026, 12).subscribe((r) => (result = r));

      const req = httpMock.expectOne((r) => r.url === reconciliationUrl);
      expect(req.request.params.get('payMonth')).toBe('12');
      req.flush(mockReport);

      // The mapper returns a NEW object, not the wire payload by reference.
      expect(result).not.toBe(mockReport as unknown as IReconciliationReport);
      expect(result).toEqual(mockReport as IReconciliationReport);
    });

    it('defaults an ABSENT attendanceFinalized to false (must not unlock Initiate)', () => {
      let result: IReconciliationReport | undefined;
      service.getReconciliation(2026, 5).subscribe((r) => (result = r));

      // The AC-4 gate: a payload that omits the flag must never read as "finalized".
      httpMock
        .expectOne((r) => r.url === reconciliationUrl)
        .flush({ period: '2026-05', payMonth: 5, payYear: 2026 });

      expect(result?.attendanceFinalized).toBeFalse();
    });

    it('defaults absent rows to an empty array and absent row numerics to 0', () => {
      let result: IReconciliationReport | undefined;
      service.getReconciliation(2026, 5).subscribe((r) => (result = r));

      httpMock
        .expectOne((r) => r.url === reconciliationUrl)
        .flush({ attendanceFinalized: true, rows: [{ employeeId: 'e-9' }] });

      expect(result?.rows.length).toBe(1);
      expect(result?.rows[0]).toEqual({
        employeeId: 'e-9',
        employeeNo: '',
        employeeName: '',
        workingDays: 0,
        presentDays: 0,
        absentDays: 0,
        leaveDaysByType: {},
        totalLeaveDays: 0,
        overtimeHours: 0,
        calculatedLopDays: 0,
      });
    });
  });

  // ─── triggerLeaveEncashment (AC-3/FR-5) ───────────────────

  describe('triggerLeaveEncashment', () => {
    const request: ILeaveEncashmentRequest = {
      employeeId: 'e-1',
      leaveTypeId: 'lt-1',
      eligibleDays: 5,
      payMonth: 5,
      payYear: 2026,
      isTaxable: true,
    };

    it('POSTs the request body to the encashments url and returns the result', () => {
      let result: ILeaveEncashmentResult | undefined;
      service.triggerLeaveEncashment(request).subscribe((r) => (result = r));

      const req = httpMock.expectOne(encashmentUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockResult);

      expect(result).toEqual(mockResult as ILeaveEncashmentResult);
    });

    it('passes through a periodDeferred result', () => {
      let result: ILeaveEncashmentResult | undefined;
      service.triggerLeaveEncashment(request).subscribe((r) => (result = r));

      httpMock
        .expectOne(encashmentUrl)
        .flush({ ...mockResult, periodDeferred: true });

      expect(result?.periodDeferred).toBeTrue();
    });

    it('defaults an ABSENT periodDeferred to false (no phantom deferral warning)', () => {
      let result: ILeaveEncashmentResult | undefined;
      service.triggerLeaveEncashment(request).subscribe((r) => (result = r));

      httpMock.expectOne(encashmentUrl).flush({ adjustmentId: 'adj-2' });

      expect(result?.periodDeferred).toBeFalse();
      expect(result?.amount).toBe(0);
    });
  });
});
