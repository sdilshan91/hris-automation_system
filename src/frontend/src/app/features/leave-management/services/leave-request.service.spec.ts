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

  // Real wire shape (LeaveRequestsLeaveRequestDto): note `id` (not leaveRequestId), `attachments` (not
  // attachmentUrls), and NO leaveTypeColor/tenantId. Flushing this exercises mapLeaveRequest.
  const wireRequest = {
    id: 'lr-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    startDate: '2026-07-06',
    endDate: '2026-07-08',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    reason: 'Vacation',
    status: 'Pending',
    requestedAt: '2026-06-13T10:00:00Z',
    createdAt: '2026-06-13T10:00:00Z',
    isLop: false,
    lopSource: null,
    attachments: ['https://files/x.pdf'],
    cancellationWindowDays: 2,
  };

  const mockBalance: ILeaveBalance = {
    leaveTypeId: 'lt-1',
    entitlementDays: 14,
    usedDays: 4,
    remainingDays: 10,
  };

  // Raw wire shape returned by GET /leaves/my-balance (backend LeaveBalanceDto).
  // getMyBalances maps entitlement->entitlementDays, used->usedDays, balance->remainingDays.
  const mockRawBalance = {
    leaveTypeId: 'lt-1',
    entitlement: 14,
    used: 4,
    balance: 10,
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

      let created: ILeaveRequest | undefined;
      service.createLeaveRequest(body).subscribe((r) => (created = r));

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(wireRequest);
      // Mapper arm — fails against the un-migrated pass-through (wire has `id`/`attachments`, not
      // `leaveRequestId`/`attachmentUrls`).
      expect(created!.leaveRequestId).toBe('lr-1');
      expect(created!.status).toBe('Pending');
      expect(created!.attachmentUrls).toEqual(['https://files/x.pdf']);
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
      req.flush({ ...wireRequest, isHalfDay: true, halfDaySession: 'AM', totalDays: 0.5 });
    });
  });

  describe('getMyLeaveRequests', () => {
    it('should GET the current employee requests', () => {
      service.getMyLeaveRequests().subscribe((reqs) => {
        expect(reqs.length).toBe(1);
        // Fails against the un-migrated pass-through (wire row has `id`, not `leaveRequestId`).
        expect(reqs[0].leaveRequestId).toBe('lr-1');
        expect(reqs[0].attachmentUrls).toEqual(['https://files/x.pdf']);
      });

      const req = httpMock.expectOne(`${baseUrl}/mine`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([wireRequest]);
    });
  });

  describe('getMyBalances', () => {
    // BUG-102 (TC-LV-256): the balances call must target the real backend route
    // `/leaves/my-balance`. It previously requested `/leaves/balances`, which 404s
    // and made the apply-leave forkJoin error out, blanking the leave-type dropdown.
    it('should GET the current employee balances and map the wire shape', () => {
      service.getMyBalances().subscribe((balances) => {
        expect(balances.length).toBe(1);
        // Mapping from the raw {entitlement,used,balance} contract.
        expect(balances[0]).toEqual(mockBalance);
        expect(balances[0].remainingDays).toBe(10);
      });

      const req = httpMock.expectOne(`${baseUrl}/my-balance`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockRawBalance]);
    });

    it('getMyBalances should GET /leaves/my-balance not /leaves/balances (BUG-102)', () => {
      service.getMyBalances().subscribe();

      // The real requested URL must be the backend route, not the legacy path.
      const req = httpMock.expectOne(`${baseUrl}/my-balance`);
      expect(req.request.method).toBe('GET');
      expect(req.request.url.endsWith('/leaves/my-balance')).toBeTrue();
      expect(req.request.url.endsWith('/leaves/balances')).toBeFalse();
      req.flush([mockRawBalance]);

      // The retired path must never be hit.
      httpMock.expectNone(`${baseUrl}/balances`);
    });
  });

  describe('cancelLeaveRequest (US-LV-010)', () => {
    it('should POST to /{id}/cancel and map the LeaveCancellationResultDto (requestId→leaveRequestId)', () => {
      let res: import('../models/leave-request.models').ILeaveCancellationResult | undefined;
      service.cancelLeaveRequest('lr-1', { reason: 'plans changed' }).subscribe((r) => (res = r));

      const req = httpMock.expectOne(`${baseUrl}/lr-1/cancel`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reason: 'plans changed' });
      expect(req.request.withCredentials).toBeTrue();
      // Real wire shape: requestId / status / balanceAfter / ledgerEntryId / cancelledAt.
      req.flush({
        requestId: 'lr-1',
        status: 'Cancelled',
        balanceAfter: 12,
        ledgerEntryId: 'led-1',
        cancelledAt: '2026-08-20T09:00:00Z',
      });
      // Fails against the un-migrated code (typed ILeaveRequest): `res.leaveRequestId` would be undefined
      // because the wire field is `requestId`.
      expect(res!.leaveRequestId).toBe('lr-1');
      expect(res!.status).toBe('Cancelled');
      expect(res!.balanceAfter).toBe(12);
    });

    it('should allow an empty reason (pending requests, BR-5)', () => {
      service.cancelLeaveRequest('lr-1', { reason: '' }).subscribe();
      const req = httpMock.expectOne(`${baseUrl}/lr-1/cancel`);
      expect(req.request.body).toEqual({ reason: '' });
      req.flush({ requestId: 'lr-1', status: 'Cancelled' });
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

describe('LeaveRequestService.parseCancelError (pure function, US-LV-010)', () => {
  it('should parse an already-started 400 body', () => {
    const err = {
      error: { message: 'Cannot cancel leave that has already started.', code: 'already_started' },
    } as HttpErrorResponse;
    const parsed = LeaveRequestService.parseCancelError(err);
    expect(parsed!.message).toContain('already started');
    expect(parsed!.code).toBe('already_started');
  });

  it('should parse a payroll-locked 400 body', () => {
    const err = {
      error: { message: 'Cannot cancel leave for a payroll-locked period.', code: 'payroll_locked' },
    } as HttpErrorResponse;
    expect(LeaveRequestService.parseCancelError(err)!.code).toBe('payroll_locked');
  });

  it('should return null for a non-object error body', () => {
    const err = { error: 'boom' } as HttpErrorResponse;
    expect(LeaveRequestService.parseCancelError(err)).toBeNull();
  });
});
