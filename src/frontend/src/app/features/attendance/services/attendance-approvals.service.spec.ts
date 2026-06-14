import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AttendanceService } from './attendance.service';
import {
  IPendingRegularization,
  IRegularizationDecisionDto,
  IBulkApproveResult,
} from '../models/attendance.models';
import { environment } from '../../../../environments/environment';

/**
 * US-ATT-004 service spec: pending-approvals fetch (envelope + bare-array unwrap),
 * single approve/reject POST body shape, bulk-approve body, and typed error parsing.
 * HttpClient is exercised via HttpTestingController — no real network.
 */
describe('AttendanceService (US-ATT-004 approvals)', () => {
  let service: AttendanceService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/attendance/regularizations`;

  const pendingItem: IPendingRegularization = {
    regularizationId: 'reg-1',
    employeeId: 'emp-1',
    employeeName: 'Ada Lovelace',
    employeePhoto: null,
    date: '2026-06-10',
    regularizationType: 'MISSED_CLOCK_IN',
    requestedClockIn: '2026-06-10T03:30:00Z',
    requestedClockOut: null,
    reason: 'Forgot to clock in this morning.',
    submittedOn: '2026-06-11T02:00:00Z',
  };

  const makeDecision = (
    id: string,
    status: 'APPROVED' | 'REJECTED',
  ): IRegularizationDecisionDto => ({
    regularizationId: id,
    status,
    action: status === 'APPROVED' ? 'APPROVE' : 'REJECT',
    approvalLevel: 1,
    attendanceLogId: status === 'APPROVED' ? 'log-1' : null,
    totalWorkMinutes: status === 'APPROVED' ? 480 : null,
    overtimeMinutes: status === 'APPROVED' ? 0 : null,
    attendanceStatus: status === 'APPROVED' ? 'COMPLETE' : null,
    actionedAt: '2026-06-11T03:00:00Z',
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AttendanceService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(AttendanceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getPendingApprovals GETs /pending and reads data.items', () => {
    let result: IPendingRegularization[] | undefined;
    service.getPendingApprovals().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/pending`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      success: true,
      data: { items: [pendingItem], totalCount: 1 },
      message: null,
    });

    expect(result?.length).toBe(1);
    expect(result?.[0].employeeName).toBe('Ada Lovelace');
  });

  it('getPendingApprovals forwards optional filter query params', () => {
    service
      .getPendingApprovals({
        employeeId: 'emp-1',
        fromDate: '2026-06-01',
        toDate: '2026-06-30',
      })
      .subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/pending` &&
        r.params.get('employeeId') === 'emp-1' &&
        r.params.get('fromDate') === '2026-06-01' &&
        r.params.get('toDate') === '2026-06-30',
    );
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: { items: [], totalCount: 0 }, message: null });
  });

  it('processRegularization(APPROVE) POSTs to /{id}/approve with { comment }', () => {
    let result: IRegularizationDecisionDto | undefined;
    service
      .processRegularization('reg-1', 'APPROVE', 'looks good')
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/reg-1/approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ comment: 'looks good' });
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ success: true, data: makeDecision('reg-1', 'APPROVED'), message: null });
    expect(result?.status).toBe('APPROVED');
  });

  it('processRegularization(APPROVE) with no comment POSTs an empty body', () => {
    service.processRegularization('reg-1', 'APPROVE').subscribe();
    const req = httpMock.expectOne(`${base}/reg-1/approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ success: true, data: makeDecision('reg-1', 'APPROVED'), message: null });
  });

  it('processRegularization(REJECT) POSTs to /{id}/reject with { reason } (not comment)', () => {
    let result: IRegularizationDecisionDto | undefined;
    service
      .processRegularization('reg-1', 'REJECT', 'Times do not match the gate logs.')
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/reg-1/reject`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Times do not match the gate logs.' });
    expect(req.request.body.comment).toBeUndefined();
    req.flush({ success: true, data: makeDecision('reg-1', 'REJECTED'), message: null });
    expect(result?.status).toBe('REJECTED');
  });

  it('bulkApprove POSTs { regularizationIds, comment } and parses items[].succeeded', () => {
    const bulkResult: IBulkApproveResult = {
      totalRequested: 2,
      succeededCount: 2,
      failedCount: 0,
      items: [
        { regularizationId: 'reg-1', succeeded: true, decision: makeDecision('reg-1', 'APPROVED') },
        { regularizationId: 'reg-2', succeeded: true, decision: makeDecision('reg-2', 'APPROVED') },
      ],
    };
    let result: IBulkApproveResult | undefined;
    service.bulkApprove(['reg-1', 'reg-2'], 'batch ok').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/bulk-approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      regularizationIds: ['reg-1', 'reg-2'],
      comment: 'batch ok',
    });
    req.flush({ success: true, data: bulkResult, message: null });
    expect(result?.items.length).toBe(2);
    expect(result?.items[0].succeeded).toBeTrue();
    expect(result?.succeededCount).toBe(2);
  });

  it('bulkApprove with no comment omits the comment field', () => {
    service.bulkApprove(['reg-1']).subscribe();
    const req = httpMock.expectOne(`${base}/bulk-approve`);
    expect(req.request.body).toEqual({ regularizationIds: ['reg-1'] });
    req.flush({
      success: true,
      data: { totalRequested: 1, succeededCount: 1, failedCount: 0, items: [] },
      message: null,
    });
  });

  describe('parseActionError', () => {
    it('extracts a typed { message, code } body (AC-5 / BR-5)', () => {
      const err = new HttpErrorResponse({
        status: 403,
        error: {
          message: 'You are not authorized to approve requests for this employee.',
          code: 'not_authorized',
        },
      });
      const parsed = AttendanceService.parseActionError(err);
      expect(parsed?.code).toBe('not_authorized');
      expect(parsed?.message).toBe(
        'You are not authorized to approve requests for this employee.',
      );
    });

    it('returns null when the body has no message', () => {
      const err = new HttpErrorResponse({ status: 500, error: 'boom' });
      expect(AttendanceService.parseActionError(err)).toBeNull();
    });
  });
});
