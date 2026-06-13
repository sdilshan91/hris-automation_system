import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveRequestService } from './leave-request.service';
import {
  ILeaveRequest,
  ICreateLeaveRequest,
  ILeaveBalance,
} from '../models/leave-request.models';
import { environment } from '../../../../environments/environment';

describe('LeaveRequestService', () => {
  let service: LeaveRequestService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/leaves`;

  const mockRequest: ILeaveRequest = {
    leaveRequestId: 'lr-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-07-06',
    endDate: '2026-07-08',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    reason: 'Vacation',
    status: 'Pending',
    requestedAt: '2026-06-13T10:00:00Z',
    attachmentUrls: [],
  };

  const mockBalance: ILeaveBalance = {
    leaveTypeId: 'lt-1',
    entitlementDays: 14,
    usedDays: 4,
    remainingDays: 10,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LeaveRequestService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LeaveRequestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('createLeaveRequest', () => {
    it('should POST a new leave request (AC-1)', () => {
      const body: ICreateLeaveRequest = {
        leaveTypeId: 'lt-1',
        startDate: '2026-07-06',
        endDate: '2026-07-08',
        isHalfDay: false,
        halfDaySession: null,
        reason: 'Vacation',
        attachments: [],
      };

      service.createLeaveRequest(body).subscribe((req) => {
        expect(req.leaveRequestId).toBe('lr-1');
        expect(req.status).toBe('Pending');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockRequest);
    });

    it('should send half-day session in the payload (AC-4)', () => {
      const body: ICreateLeaveRequest = {
        leaveTypeId: 'lt-1',
        startDate: '2026-07-06',
        endDate: '2026-07-06',
        isHalfDay: true,
        halfDaySession: 'AM',
        reason: 'Appointment',
        attachments: [],
      };

      service.createLeaveRequest(body).subscribe();

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.body.isHalfDay).toBeTrue();
      expect(req.request.body.halfDaySession).toBe('AM');
      req.flush({ ...mockRequest, isHalfDay: true, halfDaySession: 'AM', totalDays: 0.5 });
    });
  });

  describe('getMyLeaveRequests', () => {
    it('should GET the current employee requests', () => {
      service.getMyLeaveRequests().subscribe((reqs) => {
        expect(reqs.length).toBe(1);
        expect(reqs[0].leaveRequestId).toBe('lr-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/mine`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockRequest]);
    });
  });

  describe('getMyBalances', () => {
    it('should GET the current employee balances', () => {
      service.getMyBalances().subscribe((balances) => {
        expect(balances.length).toBe(1);
        expect(balances[0].remainingDays).toBe(10);
      });

      const req = httpMock.expectOne(`${baseUrl}/balances`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockBalance]);
    });
  });
});

// ─── Pure error helpers (no TestBed / httpMock.verify conflicts) ──────────

describe('LeaveRequestService.parseError (pure function)', () => {
  it('should parse a typed error response with a code', () => {
    const err = {
      error: { message: 'You already have a leave request for the selected dates', code: 'overlap' },
    } as HttpErrorResponse;
    const parsed = LeaveRequestService.parseError(err);
    expect(parsed).toBeTruthy();
    expect(parsed!.message).toContain('already have a leave request');
    expect(parsed!.code).toBe('overlap');
  });

  it('should return null for non-object error body', () => {
    const err = { error: 'string error' } as HttpErrorResponse;
    expect(LeaveRequestService.parseError(err)).toBeNull();
  });

  it('parseErrorMessage should extract message', () => {
    const err = { error: { message: 'Insufficient balance', code: 'insufficient_balance' } } as HttpErrorResponse;
    expect(LeaveRequestService.parseErrorMessage(err)).toBe('Insufficient balance');
  });

  it('parseErrorMessage should fall back for unknown shape', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(LeaveRequestService.parseErrorMessage(err)).toBe('An unexpected error occurred.');
  });
});
