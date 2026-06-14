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

  const mockEntry: ILopEntry = {
    leaveRequestId: 'lr-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    date: '2026-07-06',
    days: 1,
    source: 'system_generated',
    status: 'System-Generated',
    reason: 'No clock-in',
  };

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

  describe('getLopSummary (FR-5)', () => {
    it('GETs the lop-summary endpoint with no params', () => {
      service.getLopSummary().subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].source).toBe('system_generated');
      });
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/lop-summary`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('employeeId')).toBeNull();
      req.flush([mockEntry]);
    });

    it('includes employeeId / from / to params when provided', () => {
      service
        .getLopSummary({ employeeId: 'emp-1', from: '2026-07-01', to: '2026-07-31' })
        .subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/lop-summary` &&
          r.params.get('employeeId') === 'emp-1' &&
          r.params.get('from') === '2026-07-01' &&
          r.params.get('to') === '2026-07-31',
      );
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  describe('assignLop (FR-3)', () => {
    it('POSTs a bulk LOP assignment', () => {
      const request: IAssignLopRequest = {
        employeeId: 'emp-1',
        dates: ['2026-07-06', '2026-07-07'],
        reason: 'Unpaid absence',
      };
      service.assignLop(request).subscribe((res) => {
        expect(res.created).toBe(2);
      });
      const req = httpMock.expectOne(`${baseUrl}/assign-lop`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ employeeId: 'emp-1', created: 2 });
    });
  });

  describe('assignCompulsoryLeave (FR-6)', () => {
    it('POSTs a compulsory-leave assignment', () => {
      const request: IAssignCompulsoryLeaveRequest = {
        dates: ['2026-12-24'],
        leaveTypeId: 'lt-1',
        reason: 'Company shutdown',
        applyToAll: true,
      };
      service.assignCompulsoryLeave(request).subscribe((res) => {
        expect(res.total).toBe(10);
      });
      const req = httpMock.expectOne(`${baseUrl}/compulsory`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ deducted: 8, lop: 2, total: 10 });
    });
  });

  describe('overrideLop (BR-3)', () => {
    it('POSTs an override for a system-generated LOP entry', () => {
      const request: IOverrideLopRequest = {
        leaveTypeId: 'lt-2',
        reason: 'Employee provided medical certificate',
      };
      service.overrideLop('lr-1', request).subscribe();
      const req = httpMock.expectOne(`${baseUrl}/lop/lr-1/override`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ leaveRequestId: 'lr-1', status: 'Approved' });
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
