import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveApprovalsService } from './leave-approvals.service';
import {
  IPendingLeaveRequest,
  IPendingLeaveResponse,
  ILeaveActionResult,
} from '../models/pending-leave.models';
import { environment } from '../../../../environments/environment';

describe('LeaveApprovalsService', () => {
  let service: LeaveApprovalsService;
  let httpMock: HttpTestingController;
  const pendingUrl = `${environment.apiBaseUrl}/leaves/pending`;
  const actionResult: ILeaveActionResult = { requestId: 'lr-1', status: 'Approved' };
  const actionEnvelope = { success: true, data: actionResult, message: null };

  const mockItem: IPendingLeaveRequest = {
    requestId: 'lr-1',
    employeeId: 'emp-1',
    employeeName: 'Ada Lovelace',
    employeePhoto: null,
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-07-06',
    endDate: '2026-07-08',
    totalDays: 3,
    reason: 'Vacation',
    hasAttachments: false,
    currentBalance: 10,
    requestedAt: '2026-06-13T10:00:00Z',
    isOverdue: false,
    teamConflictCount: 0,
  };

  const mockResult: IPendingLeaveResponse = {
    items: [mockItem],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  };

  // Backend wraps the result in the standard ApiResponse<T> envelope.
  const mockEnvelope = { success: true, data: mockResult, message: null };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LeaveApprovalsService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LeaveApprovalsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should GET the pending queue, unwrap the ApiResponse envelope, with page + pageSize params', () => {
    service
      .getPendingQueue({ page: 2, pageSize: 20 })
      .subscribe((res) => {
        // Service unwraps `.data` -> caller sees the bare result.
        expect(res.items.length).toBe(1);
        expect(res.items[0].requestId).toBe('lr-1');
        expect(res.items[0].currentBalance).toBe(10);
        expect(res.totalCount).toBe(1);
      });

    const req = httpMock.expectOne(
      (r) => r.url === pendingUrl && r.params.get('page') === '2'
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.withCredentials).toBeTrue();
    // Optional filters must NOT be present when not supplied.
    expect(req.request.params.has('leaveTypeId')).toBeFalse();
    expect(req.request.params.has('employeeId')).toBeFalse();
    req.flush(mockEnvelope);
  });

  it('should include all optional filter + sort params when supplied', () => {
    service
      .getPendingQueue({
        page: 1,
        pageSize: 50,
        leaveTypeId: 'lt-9',
        employeeId: 'emp-7',
        startDate: '2026-07-01',
        endDate: '2026-07-31',
        sortBy: 'startDate',
        sortAscending: false,
      })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === pendingUrl);
    const p = req.request.params;
    expect(p.get('leaveTypeId')).toBe('lt-9');
    expect(p.get('employeeId')).toBe('emp-7');
    expect(p.get('startDate')).toBe('2026-07-01');
    expect(p.get('endDate')).toBe('2026-07-31');
    expect(p.get('sortBy')).toBe('startDate');
    expect(p.get('sortAscending')).toBe('false');
    req.flush(mockEnvelope);
  });

  it('should omit null/empty optional filters from params', () => {
    const params = service.buildParams({
      page: 1,
      pageSize: 20,
      leaveTypeId: null,
      employeeId: null,
      startDate: null,
      endDate: null,
    });
    expect(params.has('leaveTypeId')).toBeFalse();
    expect(params.has('employeeId')).toBeFalse();
    expect(params.has('startDate')).toBeFalse();
    expect(params.has('endDate')).toBeFalse();
    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
  });

  // ─── US-LV-005: approve / reject ───────────────────────────────
  describe('approve', () => {
    it('POSTs to /{id}/approve with the body + credentials and unwraps the envelope', () => {
      let result: ILeaveActionResult | undefined;
      service.approve('lr-1', { comment: 'ok' }).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/leaves/lr-1/approve`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ comment: 'ok' });
      expect(req.request.withCredentials).toBeTrue();
      req.flush(actionEnvelope);
      expect(result?.status).toBe('Approved');
    });

    it('defaults to an empty body and forwards confirmNegativeBalance', () => {
      service.approve('lr-1').subscribe();
      const empty = httpMock.expectOne(`${environment.apiBaseUrl}/leaves/lr-1/approve`);
      expect(empty.request.body).toEqual({});
      empty.flush(actionEnvelope);

      service.approve('lr-1', { confirmNegativeBalance: true }).subscribe();
      const neg = httpMock.expectOne(`${environment.apiBaseUrl}/leaves/lr-1/approve`);
      expect(neg.request.body).toEqual({ confirmNegativeBalance: true });
      neg.flush(actionEnvelope);
    });
  });

  describe('reject', () => {
    it('POSTs to /{id}/reject with the reason body + credentials', () => {
      let result: ILeaveActionResult | undefined;
      service.reject('lr-1', { reason: 'No cover' }).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/leaves/lr-1/reject`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reason: 'No cover' });
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ success: true, data: { requestId: 'lr-1', status: 'Rejected' }, message: null });
      expect(result?.status).toBe('Rejected');
    });
  });

  describe('parseActionError', () => {
    it('extracts a typed error body (code + negativeBalanceAllowed)', () => {
      const err = new HttpErrorResponse({
        status: 400,
        error: { message: 'Negative.', code: 'insufficient_balance', negativeBalanceAllowed: true },
      });
      const parsed = LeaveApprovalsService.parseActionError(err);
      expect(parsed?.code).toBe('insufficient_balance');
      expect(parsed?.negativeBalanceAllowed).toBeTrue();
      expect(parsed?.message).toBe('Negative.');
    });

    it('returns null when the body has no message', () => {
      const err = new HttpErrorResponse({ status: 500, error: 'boom' });
      expect(LeaveApprovalsService.parseActionError(err)).toBeNull();
    });
  });
});
