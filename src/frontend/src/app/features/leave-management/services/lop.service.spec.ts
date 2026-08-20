import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { LopService } from './lop.service';
import {
  ILopEntry,
  IAssignLopRequest,
  IAssignCompulsoryLeaveRequest,
  IOverrideLopRequest,
} from '../models/lop.models';
import { environment } from '../../../../environments/environment';

describe('LopService', () => {
  let service: LopService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/leaves`;


  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LopService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LopService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getLopRegister', () => {
    it('GETs the lop-register endpoint with from/to query params', () => {
      service.getLopRegister('2026-03-01', '2026-03-31').subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/lop-register` &&
          r.params.get('from') === '2026-03-01' &&
          r.params.get('to') === '2026-03-31',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([]);
    });

    it('appends employeeIds as REPEATED params, not a comma-joined value', () => {
      service.getLopRegister('2026-03-01', '2026-03-31', ['a', 'b']).subscribe();
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/lop-register`);
      // Repeated params -> getAll returns both; a comma-joined single value would be ['a,b'].
      expect(req.request.params.getAll('employeeIds')).toEqual(['a', 'b']);
      req.flush([]);
    });

    it('mapLopRegisterEntry maps requestId -> leaveRequestId and passes the rest through', () => {
      const wire = {
        requestId: 'lr-9',
        employeeId: 'emp-9',
        employeeName: 'Ann Archer',
        employeeNo: 'E-001',
        date: '2026-03-03',
        days: 3,
        source: 'HrAssigned',
        status: 'HR-Assigned',
        reason: 'Unpaid absence',
      };
      let emitted: ILopEntry[] | undefined;
      service.getLopRegister('2026-03-01', '2026-03-31').subscribe((rows) => (emitted = rows));
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/lop-register`);
      req.flush([wire]);

      expect(emitted!.length).toBe(1);
      const e = emitted![0];
      expect(e.leaveRequestId).toBe('lr-9'); // from wire `requestId`
      expect(e.employeeId).toBe('emp-9');
      expect(e.employeeName).toBe('Ann Archer');
      expect(e.employeeNo).toBe('E-001');
      expect(e.date).toBe('2026-03-03');
      expect(e.days).toBe(3);
      expect(e.source).toBe('HrAssigned');
      expect(e.status).toBe('HR-Assigned');
      expect(e.reason).toBe('Unpaid absence');
    });

    it('maps absent optional fields to their documented fallbacks, not undefined', () => {
      // employeeNo and reason absent; source absent -> 'SystemGenerated' fallback.
      const wire = {
        requestId: 'lr-10',
        employeeId: 'emp-10',
        employeeName: 'Ben Boone',
        date: '2026-03-04',
        days: 1,
        status: 'System-Generated',
      };
      let emitted: ILopEntry[] | undefined;
      service.getLopRegister('2026-03-01', '2026-03-31').subscribe((rows) => (emitted = rows));
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/lop-register`);
      req.flush([wire]);

      const e = emitted![0];
      expect(e.employeeNo).toBeNull(); // documented fallback, not undefined
      expect(e.reason).toBeNull();
      expect(e.source).toBe('SystemGenerated');
      expect(e.leaveRequestId).toBe('lr-10');
    });
  });

  describe('assignLop (FR-3)', () => {
    it('POSTs a bulk LOP assignment and maps the wire result (createdCount→created, skippedDates→skipped)', () => {
      const request: IAssignLopRequest = {
        employeeId: 'emp-1',
        dates: ['2026-07-06', '2026-07-07'],
        reason: 'Unpaid absence',
      };
      let res: import('../models/lop.models').IAssignLopResult | undefined;
      service.assignLop(request).subscribe((r) => (res = r));
      const req = httpMock.expectOne(`${baseUrl}/assign-lop`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      // Real wire shape (AssignLopResultDto): createdCount / skippedDates / leaveTypeId / requestIds.
      req.flush({
        employeeId: 'emp-1',
        leaveTypeId: 'lt-9',
        createdCount: 2,
        skippedDates: ['2026-07-07'],
        requestIds: ['r1', 'r2'],
      });
      // Fails against the un-migrated pass-through: `res.created` would be undefined (wire has `createdCount`).
      expect(res!.created).toBe(2);
      expect(res!.employeeId).toBe('emp-1');
      expect(res!.skipped).toEqual(['2026-07-07']);
    });
  });

  describe('assignCompulsoryLeave (FR-6)', () => {
    it('POSTs a compulsory-leave assignment and derives deducted = assignedCount − lopCount', () => {
      const request: IAssignCompulsoryLeaveRequest = {
        dates: ['2026-12-24'],
        leaveTypeId: 'lt-1',
        reason: 'Company shutdown',
        applyToAll: true,
      };
      let res: import('../models/lop.models').IAssignCompulsoryLeaveResult | undefined;
      service.assignCompulsoryLeave(request).subscribe((r) => (res = r));
      const req = httpMock.expectOne(`${baseUrl}/compulsory`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      // Real wire shape (CompulsoryLeaveResultDto): assignedCount / lopCount / employeesProcessed / dates.
      req.flush({
        leaveTypeId: 'lt-1',
        dates: ['2026-12-24'],
        employeesProcessed: 5,
        assignedCount: 10,
        lopCount: 2,
      });
      // Fails against the un-migrated pass-through: `res.total`/`res.deducted` would be undefined (wire has
      // assignedCount/lopCount only). The derivation keeps deducted + lop === total.
      expect(res!.total).toBe(10);
      expect(res!.lop).toBe(2);
      expect(res!.deducted).toBe(8);
    });
  });

  describe('overrideLop (BR-3)', () => {
    it('POSTs an override and maps the OverrideLopResultDto (requestId→leaveRequestId)', () => {
      const request: IOverrideLopRequest = {
        leaveTypeId: 'lt-2',
        reason: 'Employee provided medical certificate',
      };
      let res: import('../models/lop.models').IOverrideLopResult | undefined;
      service.overrideLop('lr-1', request).subscribe((r) => (res = r));
      const req = httpMock.expectOne(`${baseUrl}/lop/lr-1/override`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      // Real wire shape (OverrideLopResultDto): requestId / leaveTypeId / isLop / status / ledgerEntryId.
      req.flush({
        requestId: 'lr-1',
        leaveTypeId: 'lt-2',
        isLop: false,
        status: 'Approved',
        ledgerEntryId: 'led-7',
      });
      // Fails against the un-migrated code (typed ILeaveRequest): `res.leaveRequestId` would be undefined
      // because the wire field is `requestId`.
      expect(res!.leaveRequestId).toBe('lr-1');
      expect(res!.isLop).toBeFalse();
      expect(res!.status).toBe('Approved');
    });
  });

  describe('parseError', () => {
    it('parses a typed error body', () => {
      const err = {
        error: { message: 'Payroll locked', code: 'payroll_locked' },
      } as HttpErrorResponse;
      expect(LopService.parseError(err)!.message).toBe('Payroll locked');
      expect(LopService.parseError(err)!.code).toBe('payroll_locked');
    });

    it('returns null for a non-object body', () => {
      expect(LopService.parseError({ error: 'oops' } as HttpErrorResponse)).toBeNull();
    });

    it('parseErrorMessage falls back to a generic message', () => {
      expect(LopService.parseErrorMessage({ error: null } as HttpErrorResponse)).toBe(
        'An unexpected error occurred.',
      );
    });
  });
});
