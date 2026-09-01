import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AttendanceService } from './attendance.service';
import {
  IMonthlySummaryResult,
  IAttendanceLog,
  IClockInRequest,
  IClockStatus,
  IClockOutRequest,
  IClockOutResult,
  ICreateRegularizationRequest,
  IRegularization,
  IPendingRegularization,
  IRegularizationDecisionDto,
  IBulkApproveResult,
  IShift,
  IShiftRequest,
  IResolvedShift,
  IAssignmentResult,
  IOvertime,
  IOvertimeQueueItem,
  IOvertimeDecision,
  IOvertimeReportResult,
  ILatePolicy,
  ILateEarlyReportResult,
  ILatenessScore,
  IPeriodLock,
  IReconciliationResult,
  IAttendancePayrollResult,
  IDashboardKpi,
  ILiveBoardResult,
  IDeptComparisonResult,
  ICustomReportResult,
  ITrendsResult,
  IScheduledReportConfig,
  // ── D1 slice 3 WIRE CONTRACT ────────────────────────────────────────────────
  // `apiEnvelopeInterceptor` unwraps { success, data } globally, so EVERY `req.flush(x)`
  // in this file delivers `x` straight to a mapper: every fixture below is therefore a
  // WIRE body, not a view model. Before this slice several fixtures were view-model
  // shaped — objects the server has never sent — which is exactly how three live field
  // renames (`id` -> attendanceLogId / regularizationId) survived for months with a
  // green suite. Typing each fixture as its `…Wire` alias makes the compiler prove the
  // fixture is a payload the API can actually produce.
  AttendanceLogWire,
  ClockStatusWire,
  ClockOutResultWire,
  RegularizationWire,
  PendingRegularizationQueueWire,
  RegularizationDecisionWire,
  BulkApproveRegularizationWire,
  ShiftWire,
  ResolvedShiftWire,
  AssignmentResultWire,
  OvertimeWire,
  OvertimeQueueResultWire,
  OvertimeDecisionWire,
  OvertimeReportResultWire,
  LatePolicyWire,
  LateEarlyReportResultWire,
  LatenessScoreWire,
  AttendancePayrollResultWire,
  PeriodLockWire,
  ReconciliationResultWire,
  DashboardKpiWire,
  LiveBoardResultWire,
  DeptComparisonResultWire,
  CustomReportResultWire,
  TrendsResultWire,
  ScheduledReportConfigWire,
  formatPeriodLabel,
  periodDateRange,
} from '../models/attendance.models';
import { environment } from '../../../../environments/environment';

describe('AttendanceService', () => {
  let service: AttendanceService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/attendance`;

  /**
   * WIRE body for `AttendanceAttendanceLogDto`. The key is `id` — the rename to
   * `attendanceLogId` is the mapper's job (and was a live bug before it existed).
   * `tenantId` is ABSENT because the DTO has never carried one: the tenant is implicit
   * in the JWT + X-Tenant-Subdomain header, and echoing it into a per-employee response
   * would be a tenant-isolation smell. The old fixture set it, so every assertion that
   * read it was asserting against an object the server never sends.
   */
  const mockLog: AttendanceLogWire = {
    id: 'att-1',
    employeeId: 'emp-1',
    clockIn: '2026-06-14T08:00:00Z',
    clockOut: null,
    clockInLatitude: null,
    clockInLongitude: null,
    source: 'WEB',
    isLate: false,
    lateMinutes: 0,
    createdAt: '2026-06-14T08:00:00Z',
  };

  const mockStatus: ClockStatusWire = {
    isClockedIn: false,
    clockedInAt: null,
    requireGeolocation: false,
    shiftName: 'Day Shift',
    shiftStart: '09:00',
  };

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

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getStatus', () => {
    it('should GET the current employee clock-in status', () => {
      service.getStatus().subscribe((status) => {
        expect(status.shiftName).toBe('Day Shift');
        expect(status.isClockedIn).toBeFalse();
      });

      const req = httpMock.expectOne(`${baseUrl}/status`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockStatus);
    });

    it('fails OPEN on an empty body: requireGeolocation and isClockedIn are false', () => {
      let status: IClockStatus | undefined;
      service.getStatus().subscribe((s) => (status = s));

      const req = httpMock.expectOne(`${baseUrl}/status`);
      const empty: ClockStatusWire = {};
      req.flush(empty);

      // `requireGeolocation ?? false` is a DECISION, not an oversight. The server is the
      // authority — it rejects a coordinate-less punch with a typed 400 when the tenant
      // requires geo, and defaults the flag to false itself when no settings row exists.
      // Defaulting to `true` here would hard-block clock-in client-side for tenants where
      // geo is optional, producing NO attendance record at all — which the monthly summary
      // scores as ABSENT and converts to lost pay. That failure is silent and unrecoverable
      // by the employee; failing open is loud (a server 400 shown verbatim) and bypasses nothing.
      expect(status!.requireGeolocation).toBeFalse();
      // A wrong `false` is caught by the backend's 409 already_clocked_in guard; a wrong
      // `true` would strand the employee on a timer they cannot clock out of.
      expect(status!.isClockedIn).toBeFalse();
      expect(status!.clockedInAt).toBeNull();
      expect(status!.shiftName).toBeNull();
    });
  });

  describe('clockIn', () => {
    it('should POST a clock-in with coordinates (AC-1, AC-3 granted)', () => {
      const body: IClockInRequest = { latitude: 6.9271, longitude: 79.8612, source: 'WEB' };

      service.clockIn(body).subscribe((log) => {
        // RENAME PROOF: the wire key is `id`; the view model exposes `attendanceLogId`.
        // This assertion existed before the migration and passed VACUOUSLY — the fixture
        // was view-model shaped, so it never exercised the rename it appears to test.
        expect(log.attendanceLogId).toBe('att-1');
        // IAttendanceLog.tenantId has NO wire source at all; the mapper emits '' to keep
        // the interface satisfiable. Pinned so nobody "repairs" it into a fabricated
        // tenant key — a blank tenant id is one refactor away from being a cache key.
        expect(log.tenantId).toBe('');
        expect(log.isLate).toBeFalse();
        expect(log.lateMinutes).toBe(0);
      });

      const req = httpMock.expectOne(`${baseUrl}/clock-in`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockLog);
    });

    it('should POST a clock-in without coordinates (AC-4 geo optional)', () => {
      const body: IClockInRequest = { latitude: null, longitude: null, source: 'WEB' };

      service.clockIn(body).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/clock-in`);
      expect(req.request.body.latitude).toBeNull();
      expect(req.request.body.longitude).toBeNull();
      req.flush(mockLog);
    });

    it('narrows an unrecognised source to WEB and never invents a tenant id', () => {
      let log: IAttendanceLog | undefined;
      service
        .clockIn({ latitude: null, longitude: null, source: 'WEB' })
        .subscribe((l) => (log = l));

      const req = httpMock.expectOne(`${baseUrl}/clock-in`);
      const wire: AttendanceLogWire = { id: 'att-2', source: 'CARRIER_PIGEON' };
      req.flush(wire);

      expect(log!.attendanceLogId).toBe('att-2');
      // Guarded narrow, never a blind cast: the server validates WEB|MOBILE_WEB and the
      // entity default is "WEB". Audit-only, unrendered.
      expect(log!.source).toBe('WEB');
      expect(log!.tenantId).toBe('');
      // An absent lateness flag must claim neither a punctual arrival nor a penalty.
      expect(log!.isLate).toBeFalse();
      expect(log!.lateMinutes).toBe(0);
      expect(log!.clockOut).toBeNull();
    });
  });

  describe('clockOut (US-ATT-002)', () => {
    /**
     * WIRE body for `AttendanceClockOutResultDto`: the key is `id`, not
     * `attendanceLogId`, and the DTO also carries `employeeId` /
     * `isEarlyDeparture` / `earlyDepartureMinutes`. `overtimeMinutes` is a
     * non-nullable int on the wire (the null tri-state lives on the VM only).
     */
    const mockResult: ClockOutResultWire = {
      id: 'att-1',
      employeeId: 'emp-1',
      clockIn: '2026-06-14T03:00:00Z',
      clockOut: '2026-06-14T11:45:00Z',
      totalWorkMinutes: 465,
      overtimeMinutes: 0,
      status: 'COMPLETE',
      isEarlyDeparture: false,
      earlyDepartureMinutes: 0,
    };

    it('should POST a clock-out and return the work summary (AC-1)', () => {
      const body: IClockOutRequest = { latitude: null, longitude: null };

      service.clockOut(body).subscribe((result) => {
        expect(result.totalWorkMinutes).toBe(465);
        expect(result.status).toBe('COMPLETE');
        // RENAME PROOF: `AttendanceClockOutResultDto.id` -> `IClockOutResult.attendanceLogId`.
        expect(result.attendanceLogId).toBe('att-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/clock-out`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockResult);
    });

    it('should POST a clock-out with coordinates when geo is required (AC-5)', () => {
      const body: IClockOutRequest = { latitude: 6.9271, longitude: 79.8612 };

      service.clockOut(body).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/clock-out`);
      expect(req.request.body.latitude).toBe(6.9271);
      expect(req.request.body.longitude).toBe(79.8612);
      req.flush(mockResult);
    });

    it('routes an unknown status to ANOMALY, never COMPLETE', () => {
      let result: IClockOutResult | undefined;
      service.clockOut({ latitude: null, longitude: null }).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${baseUrl}/clock-out`);
      const wire: ClockOutResultWire = { id: 'x', status: 'SOMETHING_NEW' };
      req.flush(wire);

      // NEVER default to 'COMPLETE': that asserts a clean, finished day. ANOMALY is the
      // module's own "flagged for review" bucket, so an unknown value routes to a human.
      expect(result!.status).toBe('ANOMALY');
      expect(result!.attendanceLogId).toBe('x');
      // null = "unknown", NOT 0 = "there was no overtime". Overtime feeds pay, and the
      // card hides the OT badge on null — so null claims nothing.
      expect(result!.overtimeMinutes).toBeNull();
      expect(result!.isEarlyDeparture).toBeFalse();
      expect(result!.earlyDepartureMinutes).toBe(0);
      expect(result!.totalWorkMinutes).toBe(0);
    });
  });

  describe('regularizations (US-ATT-003)', () => {
    /**
     * WIRE body for `AttendanceRegularizationDto`. Two corrections vs the old fixture:
     * the id key is `id` (the rename to `regularizationId` is the mapper's — and it is
     * the `@for … track` key of BOTH regularization lists, so it was `undefined` on
     * every row before this migration), and `tenantId` is gone: the DTO has never
     * carried one and `IRegularization.tenantId` has now been deleted from the model.
     */
    const mockReg: RegularizationWire = {
      id: 'reg-1',
      employeeId: 'emp-1',
      attendanceLogId: null,
      date: '2026-06-10',
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '2026-06-10T03:30:00Z',
      requestedClockOut: '2026-06-10T11:30:00Z',
      reason: 'Forgot to clock in and out due to an offsite client meeting.',
      status: 'PENDING',
      createdAt: '2026-06-11T02:00:00Z',
    };

    it('should POST a regularization and return the created PENDING record (AC-1)', () => {
      const body: ICreateRegularizationRequest = {
        date: '2026-06-10',
        regularizationType: 'MISSED_BOTH',
        requestedClockIn: '09:00',
        requestedClockOut: '17:30',
        reason: 'Forgot to clock in and out due to an offsite client meeting.',
      };

      service.submitRegularization(body).subscribe((reg) => {
        // RENAME PROOF: the wire key is `id`. This assertion predates the migration and
        // passed vacuously against a view-model-shaped fixture; it now exercises the
        // rename that the employee's `@for … track req.regularizationId` depends on.
        expect(reg.regularizationId).toBe('reg-1');
        expect(reg.status).toBe('PENDING');
      });

      const req = httpMock.expectOne(`${baseUrl}/regularizations`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockReg);
    });

    it('should GET the current employee regularizations (§8)', () => {
      service.listRegularizations().subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].status).toBe('PENDING');
        // RENAME PROOF on the list path too — this is the `track` key of both the
        // desktop and mobile `@for` blocks in regularization.component.ts.
        expect(list[0].regularizationId).toBe('reg-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/regularizations`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockReg]);
    });

    it('defaults an ABSENT status to PENDING — never APPROVED', () => {
      let reg: IRegularization | undefined;
      service.listRegularizations().subscribe((rows) => (reg = rows[0]));

      const wire: RegularizationWire[] = [{ id: 'reg-2', date: '2026-06-12' }];
      httpMock.expectOne(`${baseUrl}/regularizations`).flush(wire);

      // An employee who reads "Approved" stops chasing a correction that was never
      // applied, and the corrected day never reaches payroll.
      expect(reg!.status).toBe('PENDING');
      expect(reg!.regularizationId).toBe('reg-2');
      expect(reg!.attendanceLogId).toBeNull();
      expect(reg!.requestedClockIn).toBeNull();
      expect(reg!.requestedClockOut).toBeNull();
      // Passed through UNCHANGED, not coerced to a member: regularizationTypeLabel() is an
      // exhaustive switch with no default arm, so an unknown type renders a BLANK Type cell
      // rather than confidently mislabelling a MISSED_CLOCK_IN as MISSED_BOTH.
      expect(String(reg!.regularizationType)).toBe('');
    });

    it('maps an UNRECOGNISED status to PENDING — never APPROVED', () => {
      let reg: IRegularization | undefined;
      service.listRegularizations().subscribe((rows) => (reg = rows[0]));

      const wire: RegularizationWire[] = [{ id: 'reg-3', status: 'WEIRD' }];
      httpMock.expectOne(`${baseUrl}/regularizations`).flush(wire);

      expect(reg!.status).toBe('PENDING');
      expect(reg!.regularizationId).toBe('reg-3');
    });

    it('maps a null list body to an empty array', () => {
      let rows: IRegularization[] | undefined;
      service.listRegularizations().subscribe((r) => (rows = r));
      httpMock.expectOne(`${baseUrl}/regularizations`).flush(null);
      expect(rows).toEqual([]);
    });
  });

  // ─── US-ATT-004: manager approval queue / approve / reject / bulk-approve ───
  //
  // These four calls had NO service-spec coverage before this slice, and they are the
  // highest-consequence calls in the module: a wrong id approves the wrong person's
  // request, and a wrongly-defaulted `succeeded` flag silently removes a request from
  // a manager's queue while toasting "approved".
  describe('US-ATT-004 regularization approvals', () => {
    it('getPendingApprovals reads items[] and preserves the id the approve POST uses (AC-3)', () => {
      let rows: IPendingRegularization[] | undefined;
      service.getPendingApprovals({ employeeId: 'emp-1' }).subscribe((r) => (rows = r));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/regularizations/pending` &&
          r.params.get('employeeId') === 'emp-1',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      const wire: PendingRegularizationQueueWire = {
        items: [
          {
            regularizationId: 'reg-1',
            employeeId: 'emp-1',
            employeeName: 'Ada Lovelace',
            employeePhoto: null,
            date: '2026-06-10',
            regularizationType: 'MISSED_CLOCK_IN',
            requestedClockIn: '2026-06-10T03:30:00Z',
            requestedClockOut: null,
            reason: 'Badge failed',
            submittedOn: '2026-06-11T02:00:00Z',
          },
        ],
        totalCount: 1,
      };
      req.flush(wire);

      expect(rows!.length).toBe(1);
      // NO rename on this one — `AttendancePendingRegularizationDto` already names the
      // field `regularizationId` (unlike `AttendanceRegularizationDto`, whose key is `id`).
      // It is the row `track` key, the checkbox `selectedIds` key AND the path segment of
      // approve/reject/bulk-approve: a wrong id here approves the WRONG record.
      expect(rows![0].regularizationId).toBe('reg-1');
      expect(rows![0].employeeName).toBe('Ada Lovelace');
      expect(rows![0].regularizationType).toBe('MISSED_CLOCK_IN');
      expect(rows![0].requestedClockIn).toBe('2026-06-10T03:30:00Z');
      expect(rows![0].requestedClockOut).toBeNull();
      expect(rows![0].employeePhoto).toBeNull();
      expect(rows![0].submittedOn).toBe('2026-06-11T02:00:00Z');
    });

    it('getPendingApprovals maps a queue with no items to []', () => {
      let rows: IPendingRegularization[] | undefined;
      service.getPendingApprovals().subscribe((r) => (rows = r));
      const wire: PendingRegularizationQueueWire = { totalCount: 0 };
      httpMock.expectOne(`${baseUrl}/regularizations/pending`).flush(wire);
      expect(rows).toEqual([]);
    });

    it('approveRegularization keeps an intermediate multi-level decision PENDING (AC-1)', () => {
      let decision: IRegularizationDecisionDto | undefined;
      service.approveRegularization('reg-1', 'Looks right').subscribe((d) => (decision = d));

      const req = httpMock.expectOne(`${baseUrl}/regularizations/reg-1/approve`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ comment: 'Looks right' });
      const wire: RegularizationDecisionWire = {
        regularizationId: 'reg-1',
        status: 'PENDING',
        action: 'APPROVED',
        approvalLevel: 1,
        actionedAt: '2026-06-12T02:00:00Z',
      };
      req.flush(wire);

      // The backend REALLY returns this: on an intermediate step of a multi-level workflow
      // it records the approver's decision (action = APPROVED) but leaves the regularization
      // PENDING. The VM union was widened to make that state representable — it must survive
      // the mapper rather than being collapsed to APPROVED.
      expect(decision!.status).toBe('PENDING');
      expect(decision!.action).toBe('APPROVED');
      expect(decision!.approvalLevel).toBe(1);
      // NULL, not 0: the backend leaves these unset on REJECT and on an intermediate step,
      // and `0` would read as a genuine "0 minutes worked" — a payroll-visible claim.
      expect(decision!.attendanceLogId).toBeNull();
      expect(decision!.totalWorkMinutes).toBeNull();
      expect(decision!.overtimeMinutes).toBeNull();
      expect(decision!.attendanceStatus).toBeNull();
    });

    it('approveRegularization never reads an ABSENT status as APPROVED', () => {
      let decision: IRegularizationDecisionDto | undefined;
      service.approveRegularization('reg-1').subscribe((d) => (decision = d));

      const req = httpMock.expectOne(`${baseUrl}/regularizations/reg-1/approve`);
      expect(req.request.body).toEqual({});
      const wire: RegularizationDecisionWire = { regularizationId: 'reg-1' };
      req.flush(wire);

      expect(decision!.status).toBe('PENDING');
      expect(decision!.regularizationId).toBe('reg-1');
    });

    it('rejectRegularization POSTs { reason } (NOT comment) to the reject path (AC-2)', () => {
      let decision: IRegularizationDecisionDto | undefined;
      service
        .rejectRegularization('reg-1', 'The badge log shows a successful swipe.')
        .subscribe((d) => (decision = d));

      const req = httpMock.expectOne(`${baseUrl}/regularizations/reg-1/reject`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reason: 'The badge log shows a successful swipe.' });
      const wire: RegularizationDecisionWire = {
        regularizationId: 'reg-1',
        status: 'REJECTED',
        action: 'REJECTED',
        actionedAt: '2026-06-12T02:00:00Z',
      };
      req.flush(wire);

      expect(decision!.status).toBe('REJECTED');
      // Wire values are "APPROVED"/"REJECTED", NOT "APPROVE"/"REJECT".
      expect(decision!.action).toBe('REJECTED');
    });

    it('processRegularization routes REJECT to the reject endpoint with the reason body', () => {
      service
        .processRegularization('reg-1', 'REJECT', 'A ten character reason.')
        .subscribe();
      const req = httpMock.expectOne(`${baseUrl}/regularizations/reg-1/reject`);
      expect(req.request.body).toEqual({ reason: 'A ten character reason.' });
      const wire: RegularizationDecisionWire = { regularizationId: 'reg-1', status: 'REJECTED' };
      req.flush(wire);
    });

    // The APPROVE half of the ternary at attendance.service.ts:268-271. Without this arm a
    // mutation routing EVERY action to rejectRegularization survives the whole suite — i.e.
    // every manager approval silently becomes a rejection.
    it('processRegularization routes APPROVE to the approve endpoint, never to reject', () => {
      let decision: IRegularizationDecisionDto | undefined;
      service
        .processRegularization('reg-1', 'APPROVE', 'Looks right.')
        .subscribe((d) => (decision = d));
      httpMock.expectNone(`${baseUrl}/regularizations/reg-1/reject`);
      const req = httpMock.expectOne(`${baseUrl}/regularizations/reg-1/approve`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ comment: 'Looks right.' });
      const wire: RegularizationDecisionWire = { regularizationId: 'reg-1', status: 'APPROVED' };
      req.flush(wire);
      expect(decision!.status).toBe('APPROVED');
    });

    // Positive twin for the null arms above. InstanceApproved (RegularizationApprovalService.cs
    // :452-465) really does populate all five, so the mapper must not drop them.
    it('maps every field of a final InstanceApproved decision, not just the status', () => {
      let decision: IRegularizationDecisionDto | undefined;
      service.approveRegularization('reg-9', 'ok').subscribe((d) => (decision = d));
      const wire: RegularizationDecisionWire = {
        regularizationId: 'reg-9',
        status: 'APPROVED',
        action: 'APPROVED',
        attendanceLogId: 'log-9',
        totalWorkMinutes: 480,
        overtimeMinutes: 60,
        attendanceStatus: 'PRESENT',
        comment: 'ok',
      };
      httpMock.expectOne(`${baseUrl}/regularizations/reg-9/approve`).flush(wire);
      expect(decision!.attendanceLogId).toBe('log-9');
      expect(decision!.totalWorkMinutes).toBe(480);
      expect(decision!.overtimeMinutes).toBe(60);
      expect(decision!.attendanceStatus).toBe('PRESENT');
      expect(decision!.comment).toBe('ok');
    });

    it('bulkApprove recomputes the counts from the mapped items — a wire count cannot launder an absent succeeded flag into a claimed approval (BR-7)', () => {
      let result: IBulkApproveResult | undefined;
      service.bulkApprove(['reg-1']).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${baseUrl}/regularizations/bulk-approve`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ regularizationIds: ['reg-1'] });
      // The wire CLAIMS one success in its counts but omits the per-item `succeeded` flag.
      const wire: BulkApproveRegularizationWire = {
        totalRequested: 1,
        succeededCount: 1,
        failedCount: 0,
        items: [{ regularizationId: 'reg-1' }],
      };
      req.flush(wire);

      // `onBulkSuccess` REMOVES every `succeeded` row from the manager's queue and counts it
      // into the "N request(s) approved" toast. A future flip to `?? true` would delete a
      // request that was never approved, with no second screen that would ever surface it
      // again. False leaves the row in place and shows the error toast — visibly wrong and
      // recoverable. The counts are recomputed so they can never disagree with the rows the
      // component actually removes.
      expect(result!.items[0].succeeded).toBeFalse();
      expect(result!.succeededCount).toBe(0);
      expect(result!.failedCount).toBe(1);
      // `totalRequested` DOES come from the wire — the backend de-duplicates ids, so it is
      // the only honest source for "how many distinct ids were processed".
      expect(result!.totalRequested).toBe(1);
      // A failed item genuinely has no decision; do not synthesise an empty one.
      expect(result!.items[0].decision).toBeUndefined();
    });

    it('bulkApprove counts a genuinely succeeded item and maps its embedded decision', () => {
      let result: IBulkApproveResult | undefined;
      service.bulkApprove(['reg-1', 'reg-2'], 'Batch approved').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${baseUrl}/regularizations/bulk-approve`);
      expect(req.request.body).toEqual({
        regularizationIds: ['reg-1', 'reg-2'],
        comment: 'Batch approved',
      });
      const wire: BulkApproveRegularizationWire = {
        totalRequested: 2,
        items: [
          {
            regularizationId: 'reg-1',
            succeeded: true,
            decision: {
              regularizationId: 'reg-1',
              status: 'APPROVED',
              action: 'APPROVED',
              approvalLevel: 1,
              totalWorkMinutes: 480,
            },
          },
          {
            regularizationId: 'reg-2',
            succeeded: false,
            error: 'Payroll period is locked.',
            errorCode: 'payroll_period_locked',
          },
        ],
      };
      req.flush(wire);

      expect(result!.succeededCount).toBe(1);
      expect(result!.failedCount).toBe(1);
      expect(result!.totalRequested).toBe(2);
      expect(result!.items[0].decision!.status).toBe('APPROVED');
      expect(result!.items[0].decision!.totalWorkMinutes).toBe(480);
      expect(result!.items[1].succeeded).toBeFalse();
      expect(result!.items[1].errorCode).toBe('payroll_period_locked');
    });
  });

  // ─── US-ATT-005: Shift management & assignment ──────────────────────────────
  describe('US-ATT-005 shift endpoints', () => {
    /**
     * WIRE body for `AttendanceShiftDto`. The literal is unchanged from the old
     * view-model fixture — this concern has NO renames; every IShift field has an
     * identically-named wire field. The value of the migration here is entirely in the
     * defaults and the guarded narrows, which the tests below pin.
     */
    const mockShift: ShiftWire = {
      id: 'shift-1',
      name: 'Morning Shift',
      type: 'SINGLE',
      startTime: '09:00',
      endTime: '17:00',
      breakDurationMinutes: 60,
      gracePeriodMinutes: 10,
      minimumHours: null,
      workingDays: [1, 2, 3, 4, 5],
      isDefault: true,
      isActive: true,
      assignedEmployeeCount: 3,
    };

    it('getShifts unwraps the ApiResponse envelope to ShiftDto[]', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((s) => (result = s));

      const req = httpMock.expectOne(`${baseUrl}/shifts`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockShift]);

      expect(result!.length).toBe(1);
      expect(result![0].name).toBe('Morning Shift');
      // Guarded narrow, not a blind cast: 'SINGLE' is the entity default and the only
      // fallback that claims nothing (ROTATING would open a rotation editor on a shift
      // with no rotation; FLEXIBLE would hide start/end columns on a shift that has them).
      expect(result![0].type).toBe('SINGLE');
      // A SINGLE shift has no rotation — `undefined` is what the shift form branches on.
      expect(result![0].rotation).toBeUndefined();
    });

    // Positive guardian for the `rotation` absence arms above. Without it, mapRotation and
    // mapRotationStep could both be DELETED and the whole suite would still pass — the
    // ROTATING shift editor would silently lose its pattern.
    it('maps a ROTATING shift rotation and its ordered steps', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((sh) => (result = sh));

      const wire: ShiftWire[] = [
        {
          id: 's12',
          name: 'Rotating Crew',
          type: 'ROTATING',
          rotation: {
            cycleLengthDays: 14,
            referenceStartDate: '2026-06-01',
            steps: [
              { order: 1, shiftId: 'shift-1', durationDays: 7 },
              { order: 2, shiftId: 'shift-2', durationDays: 7 },
            ],
          },
        },
      ];
      httpMock.expectOne(`${baseUrl}/shifts`).flush(wire);

      expect(result![0].type).toBe('ROTATING');
      const rotation = result![0].rotation;
      expect(rotation).toBeDefined();
      expect(rotation!.cycleLengthDays).toBe(14);
      expect(rotation!.referenceStartDate).toBe('2026-06-01');
      expect(rotation!.steps.length).toBe(2);
      // Step order is what the editor renders the cycle by — a dropped or reordered step
      // silently changes which shift an employee works.
      expect(rotation!.steps[0].shiftId).toBe('shift-1');
      expect(rotation!.steps[0].order).toBe(1);
      expect(rotation!.steps[0].durationDays).toBe(7);
      expect(rotation!.steps[1].shiftId).toBe('shift-2');
    });

    it('getShifts under-claims every absent flag (not default, not active, no days)', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((sh) => (result = sh));

      const wire: ShiftWire[] = [{ id: 's9' }];
      httpMock.expectOne(`${baseUrl}/shifts`).flush(wire);

      expect(result![0].id).toBe('s9');
      // An absent flag must never paint every shift with the tenant "Default" badge...
      expect(result![0].isDefault).toBeFalse();
      // ...nor claim a shift is live and schedulable.
      expect(result![0].isActive).toBeFalse();
      // Never claim assignments that did not happen.
      expect(result![0].assignedEmployeeCount).toBe(0);
      // `[]` renders "—" via formatWorkingDays.
      expect(result![0].workingDays).toEqual([]);
      expect(result![0].type).toBe('SINGLE');
      expect(result![0].rotation).toBeUndefined();
      expect(result![0].startTime).toBeNull();
      expect(result![0].endTime).toBeNull();
    });

    it('getShifts falls back to SINGLE for an unrecognised shift type', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((sh) => (result = sh));

      const wire: ShiftWire[] = [{ id: 's10', type: 'BOGUS' }];
      httpMock.expectOne(`${baseUrl}/shifts`).flush(wire);

      expect(result![0].type).toBe('SINGLE');
    });

    it('getShifts treats a null rotation as undefined — the backend really sends null', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((sh) => (result = sh));

      // The generated type declares `rotation?: AttendanceRotationDto` with NO `| null`,
      // but ShiftService.ToDto emits `Rotation = null` for every SINGLE/FLEXIBLE shift.
      // Hence the mapper's truthiness check rather than an `undefined` check; the local
      // type override below documents that the generated contract is optimistic here.
      const wire: (Omit<ShiftWire, 'rotation'> & { rotation: null })[] = [
        { id: 's11', type: 'FLEXIBLE', rotation: null },
      ];
      httpMock.expectOne(`${baseUrl}/shifts`).flush(wire);

      expect(result![0].rotation).toBeUndefined();
      expect(result![0].type).toBe('FLEXIBLE');
    });

    it('createShift POSTs the request and unwraps data', () => {
      const body: IShiftRequest = {
        name: 'Night Shift',
        type: 'SINGLE',
        startTime: '22:00',
        endTime: '06:00',
        breakDurationMinutes: 30,
        gracePeriodMinutes: 15,
        workingDays: [1, 2, 3, 4, 5],
      };
      let result: IShift | undefined;
      service.createShift(body).subscribe((s) => (result = s));

      const req = httpMock.expectOne(`${baseUrl}/shifts`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ ...mockShift, name: 'Night Shift' });
      expect(result!.name).toBe('Night Shift');
    });

    // NOTE: this request body is a KNOWN LIVE DEFECT, deliberately left as-is.
    // It sends a FLEXIBLE shift with `workingDays: []`, which the backend now REJECTS
    // with a 400 (ShiftRequestValidator requires at least one working day). Fixing the
    // fixture alone would make a green test certify a payload the API refuses; the
    // component that builds this body has to be fixed in the same change. Do not
    // "correct" it here in isolation — see the shift concern's FINDINGS.
    it('updateShift PUTs to the id path', () => {
      const body: IShiftRequest = {
        name: 'Updated',
        type: 'FLEXIBLE',
        breakDurationMinutes: 0,
        gracePeriodMinutes: 0,
        minimumHours: 8,
        workingDays: [],
      };
      service.updateShift('shift-1', body).subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(mockShift);
    });

    it('deleteShift DELETEs the id path (204)', () => {
      service.deleteShift('shift-1').subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('cloneShift POSTs to the clone path', () => {
      service.cloneShift('shift-1').subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1/clone`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...mockShift, id: 'shift-2', name: 'Morning Shift (copy)' });
    });

    it('assignShift POSTs employeeIds + effectiveFrom and unwraps the result', () => {
      let result: { assignedCount: number } | undefined;
      service
        .assignShift('shift-1', { employeeIds: ['e1', 'e2'], effectiveFrom: '2026-07-01' })
        .subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1/assign`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ employeeIds: ['e1', 'e2'], effectiveFrom: '2026-07-01' });
      req.flush({ assignedCount: 2, employeeShiftIds: ['es1', 'es2'] });
      expect(result!.assignedCount).toBe(2);
    });

    it('assignShift never claims assignments that did not happen', () => {
      let result: IAssignmentResult | undefined;
      service
        .assignShift('shift-1', { employeeIds: ['e1'], effectiveFrom: '2026-07-01' })
        .subscribe((r) => (result = r));

      const empty: AssignmentResultWire = {};
      httpMock.expectOne(`${baseUrl}/shifts/shift-1/assign`).flush(empty);

      // `assignedCount` is rendered verbatim in the success toast AND added to the row's
      // assignedEmployeeCount, so an absent count must announce nothing.
      expect(result!.assignedCount).toBe(0);
      expect(result!.employeeShiftIds).toEqual([]);
    });

    it('getResolvedShift GETs with the date query param', () => {
      let resolved: IResolvedShift | undefined;
      service.getResolvedShift('emp-9', '2026-07-01').subscribe((r) => (resolved = r));
      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/employees/emp-9/shift` && r.params.get('date') === '2026-07-01',
      );
      expect(req.request.method).toBe('GET');
      const wire: ResolvedShiftWire = {
        ...mockShift,
        effectiveFrom: '2026-06-01',
        effectiveTo: null,
        resolvedForDate: '2026-07-01',
      };
      req.flush(wire);

      // The three resolution-only fields (AttendanceResolvedShiftDto is a structural
      // superset of AttendanceShiftDto)...
      expect(resolved!.effectiveFrom).toBe('2026-06-01');
      expect(resolved!.effectiveTo).toBeNull();
      expect(resolved!.resolvedForDate).toBe('2026-07-01');
      // ...and every INHERITED shift field survives the shared mapper.
      expect(resolved!.id).toBe('shift-1');
      expect(resolved!.name).toBe('Morning Shift');
      expect(resolved!.type).toBe('SINGLE');
      expect(resolved!.startTime).toBe('09:00');
      expect(resolved!.endTime).toBe('17:00');
      expect(resolved!.breakDurationMinutes).toBe(60);
      expect(resolved!.gracePeriodMinutes).toBe(10);
      expect(resolved!.workingDays).toEqual([1, 2, 3, 4, 5]);
      expect(resolved!.isDefault).toBeTrue();
      expect(resolved!.isActive).toBeTrue();
    });

    it('getResolvedShift blanks effectiveFrom rather than fabricating a window', () => {
      let resolved: IResolvedShift | undefined;
      service.getResolvedShift('emp-9', '2026-07-01').subscribe((r) => (resolved = r));

      // Genuinely null on the wire when the resolution fell back to the tenant default
      // shift (C# `ResolvedShiftDto.EffectiveFrom` is `DateOnly?`).
      const wire: ResolvedShiftWire = { id: 'shift-default', resolvedForDate: '2026-07-01' };
      httpMock
        .expectOne((r) => r.url === `${baseUrl}/employees/emp-9/shift`)
        .flush(wire);

      expect(resolved!.effectiveFrom).toBe('');
      expect(resolved!.effectiveTo).toBeNull();
      expect(resolved!.resolvedForDate).toBe('2026-07-01');
    });
  });


  // ─── US-ATT-006: overtime tracking & approval ───────────────────────────────
  //
  // There was ZERO service coverage for this story before this slice. THIS SURFACE
  // DRIVES PAY: every assertion below pins a default whose opposite direction either
  // pays unapproved overtime or silently zeroes approved overtime.
  describe('US-ATT-006 overtime', () => {
    it('getMyOvertime keeps approvedMinutes NULL on a PENDING record — never awards the detected minutes', () => {
      let rows: IOvertime[] | undefined;
      service.getMyOvertime().subscribe((r) => (rows = r));

      const req = httpMock.expectOne(`${baseUrl}/overtime/my`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      const wire: OvertimeWire[] = [
        {
          id: 'ot-1',
          status: 'PENDING',
          overtimeMinutes: 120,
          approvedMinutes: null,
          multiplier: 1.5,
          type: 'AUTO_DETECTED',
          date: '2026-06-08',
          employeeId: 'e1',
          attendanceLogId: 'l1',
          createdAt: '2026-06-08T12:00:00Z',
          dailyCapApplied: false,
          weeklyCapExceeded: false,
        },
      ];
      req.flush(wire);

      expect(rows!.length).toBe(1);
      // No fallback to any other field: my-overtime tracks rows by `ot.id` and the
      // approvals queue POSTs it to /overtime/{id}/approve. An empty id fails loudly.
      expect(rows![0].id).toBe('ot-1');
      // PAY-CRITICAL. `?? w.overtimeMinutes` would award unapproved overtime and report a
      // PENDING record as approved; `?? 0` would silently zero an approved one.
      expect(rows![0].approvedMinutes).toBeNull();
      expect(rows![0].overtimeMinutes).toBe(120);
      expect(rows![0].status).toBe('PENDING');
      expect(rows![0].type).toBe('AUTO_DETECTED');
      expect(rows![0].multiplier).toBe(1.5);
      expect(rows![0].attendanceLogId).toBe('l1');
    });

    it('getMyOvertime does not zero an APPROVED record', () => {
      let rows: IOvertime[] | undefined;
      service.getMyOvertime().subscribe((r) => (rows = r));

      const wire: OvertimeWire[] = [
        { id: 'ot-2', status: 'APPROVED', overtimeMinutes: 120, approvedMinutes: 90 },
      ];
      httpMock.expectOne(`${baseUrl}/overtime/my`).flush(wire);

      expect(rows![0].approvedMinutes).toBe(90);
      expect(rows![0].status).toBe('APPROVED');
    });

    it('getMyOvertime under-claims every absent field, and uses NaN for an unknown multiplier', () => {
      let rows: IOvertime[] | undefined;
      service.getMyOvertime().subscribe((r) => (rows = r));

      const wire: OvertimeWire[] = [{ id: 'ot-3' }];
      httpMock.expectOne(`${baseUrl}/overtime/my`).flush(wire);

      // Never APPROVED, and PENDING also counts toward the weekly cap bar (so the bar
      // warns earlier rather than later).
      expect(rows![0].status).toBe('PENDING');
      expect(rows![0].approvedMinutes).toBeNull();
      // 0, never a guess — this is also the [max] of the manager's "adjust awarded
      // minutes" input, so 0 blocks an adjustment rather than authorising invented minutes.
      expect(rows![0].overtimeMinutes).toBe(0);
      // 'AUTO_DETECTED' is the less-claiming of the two: 'PRE_APPROVED' would assert the
      // employee obtained permission in advance.
      expect(rows![0].type).toBe('AUTO_DETECTED');
      // NaN is DELIBERATE and is the only value in `number` that cannot be misread as a
      // rate: `0` renders "0x" ("worth nothing") and `1` silently renders straight time —
      // the most dangerous option because it looks like a legitimate rate. NaN renders
      // "—" through formatMultiplier()'s existing NaN branch.
      expect(rows![0].multiplier).toBeNaN();
    });

    it('getMyOvertime maps a null body to an empty list', () => {
      let rows: IOvertime[] | undefined;
      service.getMyOvertime().subscribe((r) => (rows = r));
      httpMock.expectOne(`${baseUrl}/overtime/my`).flush(null);
      expect(rows).toEqual([]);
    });

    it('getPendingOvertime reads items[] and keeps submittedOn distinct from createdAt (AC-3)', () => {
      let rows: IOvertimeQueueItem[] | undefined;
      service.getPendingOvertime().subscribe((r) => (rows = r));

      const req = httpMock.expectOne(`${baseUrl}/overtime/pending`);
      expect(req.request.method).toBe('GET');
      const wire: OvertimeQueueResultWire = {
        items: [
          {
            id: 'q1',
            employeeName: 'Ada',
            submittedOn: '2026-06-10T18:05:00Z',
            createdAt: '2026-06-10T18:00:00Z',
          },
        ],
        totalCount: 1,
      };
      req.flush(wire);

      expect(rows!.length).toBe(1);
      // This is the id the approve POST uses — a wrong one approves the wrong person's overtime.
      expect(rows![0].id).toBe('q1');
      expect(rows![0].employeeName).toBe('Ada');
      // Submission vs record creation are DIFFERENT timestamps on the wire; keep them apart.
      expect(rows![0].submittedOn).toBe('2026-06-10T18:05:00Z');
      expect(rows![0].createdAt).toBe('2026-06-10T18:00:00Z');
      expect(rows![0].employeePhoto).toBeNull();
      // The shared half of the mapper applies here too.
      expect(rows![0].status).toBe('PENDING');
      expect(rows![0].approvedMinutes).toBeNull();
    });

    it('getPendingOvertime maps a queue with no items to []', () => {
      let rows: IOvertimeQueueItem[] | undefined;
      service.getPendingOvertime().subscribe((r) => (rows = r));
      const wire: OvertimeQueueResultWire = { totalCount: 0 };
      httpMock.expectOne(`${baseUrl}/overtime/pending`).flush(wire);
      expect(rows).toEqual([]);
    });

    it('approveOvertime POSTs { approvedMinutes, comment } and never upgrades an absent status to APPROVED (FR-6)', () => {
      let decision: IOvertimeDecision | undefined;
      service.approveOvertime('ot-1', 90, 'ok').subscribe((d) => (decision = d));

      const req = httpMock.expectOne(`${baseUrl}/overtime/ot-1/approve`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ approvedMinutes: 90, comment: 'ok' });
      expect(req.request.withCredentials).toBeTrue();
      const wire: OvertimeDecisionWire = { id: 'ot-1' };
      req.flush(wire);

      expect(decision!.id).toBe('ot-1');
      // The backend emits APPROVED|REJECTED here, but an ABSENT status must never read as
      // APPROVED — that is the whole point of the shared 'PENDING' fallback.
      expect(decision!.status).toBe('PENDING');
      expect(decision!.approvedMinutes).toBeNull();
      expect(decision!.multiplier).toBeNaN();
    });

    it('approveOvertime omits approvedMinutes from the body when none is supplied', () => {
      service.approveOvertime('ot-1').subscribe();
      const req = httpMock.expectOne(`${baseUrl}/overtime/ot-1/approve`);
      expect(req.request.body).toEqual({});
      const wire: OvertimeDecisionWire = { id: 'ot-1', status: 'APPROVED', approvedMinutes: 120 };
      req.flush(wire);
    });

    it('rejectOvertime POSTs { reason } and keeps approvedMinutes null', () => {
      let decision: IOvertimeDecision | undefined;
      service
        .rejectOvertime('ot-1', 'Not authorised in advance.')
        .subscribe((d) => (decision = d));

      const req = httpMock.expectOne(`${baseUrl}/overtime/ot-1/reject`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reason: 'Not authorised in advance.' });
      const wire: OvertimeDecisionWire = {
        id: 'ot-1',
        status: 'REJECTED',
        approvedMinutes: null,
        actionedAt: '2026-06-11T02:00:00Z',
      };
      req.flush(wire);

      expect(decision!.status).toBe('REJECTED');
      expect(decision!.approvedMinutes).toBeNull();
      expect(decision!.actionedAt).toBe('2026-06-11T02:00:00Z');
    });

    it('getOvertimeReport reads the items[] key (NOT rows) plus the totals footer (AC-5)', () => {
      let report: IOvertimeReportResult | undefined;
      service.getOvertimeReport('2026-06').subscribe((r) => (report = r));

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/overtime/report` && r.params.get('month') === '2026-06',
      );
      expect(req.request.method).toBe('GET');
      // The array key here is `items`, unlike the sibling attendance report DTOs which
      // use `rows` — reading the wrong key renders an empty report with no error.
      const wire: OvertimeReportResultWire = {
        month: '2026-06',
        items: [
          {
            employeeId: 'e1',
            employeeName: 'Ada Lovelace',
            approvedMinutes: 600,
            pendingMinutes: 120,
            rejectedMinutes: 0,
            recordCount: 4,
          },
        ],
        totals: {
          approvedMinutes: 600,
          pendingMinutes: 120,
          rejectedMinutes: 0,
          recordCount: 4,
        },
      };
      req.flush(wire);

      expect(report!.month).toBe('2026-06');
      expect(report!.items.length).toBe(1);
      expect(report!.items[0].employeeName).toBe('Ada Lovelace');
      expect(report!.items[0].approvedMinutes).toBe(600);
      expect(report!.totals.approvedMinutes).toBe(600);
      expect(report!.totals.pendingMinutes).toBe(120);
      expect(report!.totals.recordCount).toBe(4);
    });

    it('getOvertimeReport zeroes the totals footer when the wire omits totals', () => {
      let report: IOvertimeReportResult | undefined;
      service.getOvertimeReport('2026-06').subscribe((r) => (report = r));

      const wire: OvertimeReportResultWire = { month: '2026-06', items: [] };
      httpMock.expectOne((r) => r.url === `${baseUrl}/overtime/report`).flush(wire);

      // An all-zero footer under-claims and is visibly inconsistent with non-zero rows.
      // Summing the rows here would invent a tenant-wide total the backend never sent.
      expect(report!.items).toEqual([]);
      expect(report!.totals).toEqual({
        approvedMinutes: 0,
        pendingMinutes: 0,
        rejectedMinutes: 0,
        recordCount: 0,
      });
    });
  });

  // ─── US-ATT-008: late policy, late/early report, lateness score ─────────────
  //
  // US-ATT-008 had ZERO service coverage before this slice — no late-policy test and no
  // late-early test. `deductionDays` is MONEY: an absent flag must not switch late
  // deductions on, and must not invent a deduction.
  describe('US-ATT-008 late policy & lateness', () => {
    const mockPolicy: LatePolicyWire = {
      thresholdCount: 3,
      deductionDays: 0.5,
      period: 'MONTHLY',
      notificationOnLate: true,
      chronicThreshold: 5,
      isActive: true,
    };

    it('getLatePolicy GETs the tenant policy and round-trips every field (FR-4)', () => {
      let policy: ILatePolicy | undefined;
      service.getLatePolicy().subscribe((p) => (policy = p));

      const req = httpMock.expectOne(`${baseUrl}/late-policy`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockPolicy);

      expect(policy).toEqual({
        thresholdCount: 3,
        deductionDays: 0.5,
        period: 'MONTHLY',
        notificationOnLate: true,
        chronicThreshold: 5,
        isActive: true,
      });
    });

    it('getLatePolicy neither switches deductions on nor invents one on an empty body (MONEY)', () => {
      let policy: ILatePolicy | undefined;
      service.getLatePolicy().subscribe((p) => (policy = p));

      const empty: LatePolicyWire = {};
      httpMock.expectOne(`${baseUrl}/late-policy`).flush(empty);

      // An absent flag must NEVER assert that late deductions are live.
      expect(policy!.isActive).toBeFalse();
      // Money. 0 = "no day is deducted"; any non-zero fallback would invent a deduction.
      expect(policy!.deductionDays).toBe(0);
      expect(policy!.period).toBe('MONTHLY');
      expect(policy!.notificationOnLate).toBeFalse();
      // 0 is deliberately OUT OF RANGE for the policy form's Validators.min(1), so an
      // absent threshold surfaces as a visible validation error rather than silently
      // persisting a fabricated number on the next save.
      expect(policy!.thresholdCount).toBe(0);
      expect(policy!.chronicThreshold).toBe(0);
    });

    it('getLatePolicy narrows an unrecognised period to MONTHLY (the guarded narrow)', () => {
      let policy: ILatePolicy | undefined;
      service.getLatePolicy().subscribe((p) => (policy = p));

      const wire: LatePolicyWire = { period: 'WEEKLY' };
      httpMock.expectOne(`${baseUrl}/late-policy`).flush(wire);

      // MONTHLY is both the more permissive window (the threshold resets more often) and
      // what latePolicyPeriodLabel already falls back to for anything not 'QUARTERLY'.
      expect(policy!.period).toBe('MONTHLY');
    });

    it('updateLatePolicy PUTs the policy verbatim as the request body (FR-4)', () => {
      const policy: ILatePolicy = {
        thresholdCount: 4,
        deductionDays: 1,
        period: 'QUARTERLY',
        notificationOnLate: false,
        chronicThreshold: 8,
        isActive: true,
      };
      let saved: ILatePolicy | undefined;
      service.updateLatePolicy(policy).subscribe((p) => (saved = p));

      const req = httpMock.expectOne(`${baseUrl}/late-policy`);
      expect(req.request.method).toBe('PUT');
      // This is the only write in the module that ships a view model straight back as the
      // body, so the request shape is pinned here as well as by `satisfies LatePolicyWire`.
      expect(req.request.body).toEqual(policy);
      expect(req.request.withCredentials).toBeTrue();
      const echoed: LatePolicyWire = { ...policy };
      req.flush(echoed);

      expect(saved!.period).toBe('QUARTERLY');
      expect(saved!.deductionDays).toBe(1);
      expect(saved!.isActive).toBeTrue();
    });

    it('getLateEarlyReport must not brand an employee chronic on an absent flag (FR-7)', () => {
      let report: ILateEarlyReportResult | undefined;
      service
        .getLateEarlyReport({ from: '2026-06-01', to: '2026-06-30', scope: 'all' })
        .subscribe((r) => (report = r));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/late-early/report` &&
          r.params.get('from') === '2026-06-01' &&
          r.params.get('to') === '2026-06-30' &&
          r.params.get('scope') === 'all',
      );
      expect(req.request.method).toBe('GET');
      const wire: LateEarlyReportResultWire = {
        from: '2026-06-01',
        to: '2026-06-30',
        rows: [
          {
            employeeId: 'e1',
            employeeName: 'Ada Lovelace',
            lateCount: 2,
            totalLateMinutes: 25,
          },
        ],
      };
      req.flush(wire);

      expect(report!.from).toBe('2026-06-01');
      expect(report!.rows.length).toBe(1);
      // `isChronic` drives an amber row highlight, a "Chronic" badge and a CSV Yes/No
      // column. It is the one boolean in this slice that fails OPEN: false = no accusation.
      expect(report!.rows[0].isChronic).toBeFalse();
      expect(report!.rows[0].departmentName).toBeNull();
      expect(report!.rows[0].earlyDepartureCount).toBe(0);
      expect(report!.rows[0].totalEarlyMinutes).toBe(0);
    });

    it('getLateEarlyReport maps an empty body to an empty report', () => {
      let report: ILateEarlyReportResult | undefined;
      service
        .getLateEarlyReport({ from: '2026-06-01', to: '2026-06-30' })
        .subscribe((r) => (report = r));

      const empty: LateEarlyReportResultWire = {};
      httpMock.expectOne((r) => r.url === `${baseUrl}/late-early/report`).flush(empty);

      expect(report).toEqual({ from: '', to: '', rows: [] });
    });

    it('getMyLatenessScore GETs by month and round-trips the score (§8, AC-4)', () => {
      let score: ILatenessScore | undefined;
      service.getMyLatenessScore('2026-06').subscribe((sc) => (score = sc));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/late-early/my-score` && r.params.get('month') === '2026-06',
      );
      expect(req.request.method).toBe('GET');
      const wire: LatenessScoreWire = {
        yearMonth: '2026-06',
        lateCount: 2,
        allowedLates: 3,
        earlyDepartureCount: 1,
      };
      req.flush(wire);

      expect(score).toEqual({
        yearMonth: '2026-06',
        lateCount: 2,
        allowedLates: 3,
        earlyDepartureCount: 1,
      });
    });

    it('getMyLatenessScore zeroes every counter on an empty body', () => {
      let score: ILatenessScore | undefined;
      service.getMyLatenessScore('2026-06').subscribe((sc) => (score = sc));

      const empty: LatenessScoreWire = {};
      httpMock.expectOne((r) => r.url === `${baseUrl}/late-early/my-score`).flush(empty);

      // `allowedLates: 0` is read by latenessScoreTone/latenessUsedPercent as "no allowance
      // configured" (amber, full bar on the first late) — the existing deliberate handling
      // of an unset allowance. Any positive fallback would invent an allowance the policy
      // never granted.
      expect(score).toEqual({
        yearMonth: '',
        lateCount: 0,
        allowedLates: 0,
        earlyDepartureCount: 0,
      });
    });
  });

  // ─── US-ATT-007: Monthly attendance summary ────────────────
  describe('US-ATT-007 monthly summary', () => {
    it('getMonthlySummary GETs with the month + filter params and unwraps data', () => {
      let res: { yearMonth: string } | undefined;
      service
        .getMonthlySummary({ month: '2026-06', departmentId: 'd1', shiftId: 's1' })
        .subscribe((r) => (res = r));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/summary/monthly` &&
          r.params.get('month') === '2026-06' &&
          r.params.get('departmentId') === 'd1' &&
          r.params.get('shiftId') === 's1',
      );
      expect(req.request.method).toBe('GET');
      req.flush({
        yearMonth: '2026-06',
        rows: [],
        banner: { totalEmployees: 0, averageAttendancePercent: 0, totalLopDays: 0 },
        generatedAt: '2026-06-01T01:00:00Z',
      });
      expect(res!.yearMonth).toBe('2026-06');
    });

    // Paired mapper arms. The component spec builds the banner directly, so it can only
    // guard the RENDER; without these two the mapper default could revert to `?? 0`
    // undetected (verified by mutation, 2026-09-01).
    it('maps an absent averageAttendancePercent to null, never to 0', () => {
      let res: IMonthlySummaryResult | undefined;
      service.getMonthlySummary({ month: '2026-06' }).subscribe((r) => (res = r));
      httpMock.expectOne((r) => r.url === `${baseUrl}/summary/monthly`).flush({
        yearMonth: '2026-06',
        rows: [],
        banner: { totalEmployees: 4, totalLopDays: 0 },
        generatedAt: null,
      });
      // 0% would be a headline claim that nobody attended; absent must claim nothing.
      expect(res!.banner.averageAttendancePercent).toBeNull();
    });

    it('passes a real averageAttendancePercent of 0 through untouched', () => {
      let res: IMonthlySummaryResult | undefined;
      service.getMonthlySummary({ month: '2026-06' }).subscribe((r) => (res = r));
      httpMock.expectOne((r) => r.url === `${baseUrl}/summary/monthly`).flush({
        yearMonth: '2026-06',
        rows: [],
        banner: { totalEmployees: 4, averageAttendancePercent: 0, totalLopDays: 0 },
        generatedAt: null,
      });
      expect(res!.banner.averageAttendancePercent).toBe(0);
    });

    it('getEmployeeDailyBreakdown GETs the employee path with the month param', () => {
      service.getEmployeeDailyBreakdown('emp-7', '2026-06').subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/summary/monthly/emp-7` &&
          r.params.get('month') === '2026-06',
      );
      expect(req.request.method).toBe('GET');
      req.flush({ employeeId: 'emp-7', employeeName: 'X', yearMonth: '2026-06', days: [] });
    });

    it('generateMonthlySummary POSTs to the generate path with the month param', () => {
      let status: { status: string } | undefined;
      service.generateMonthlySummary('2026-06').subscribe((s) => (status = s));
      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/summary/monthly/generate` &&
          r.params.get('month') === '2026-06',
      );
      expect(req.request.method).toBe('POST');
      req.flush({ yearMonth: '2026-06', status: 'RUNNING', generatedAt: null });
      expect(status!.status).toBe('RUNNING');
    });

    it('exportMonthlySummary GETs the export path as a blob with format + filters', () => {
      let resp: { body: Blob | null } | undefined;
      service
        .exportMonthlySummary({ month: '2026-06', departmentId: 'd1' }, 'xlsx')
        .subscribe((r) => (resp = r));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/summary/monthly/export` &&
          r.params.get('month') === '2026-06' &&
          r.params.get('format') === 'xlsx' &&
          r.params.get('departmentId') === 'd1',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['data'], { type: 'application/octet-stream' }));
      expect(resp!.body).toBeTruthy();
    });
  });

  // ─── US-ATT-009: payroll integration ──────────────────────────
  describe('US-ATT-009 payroll integration', () => {
    // WIRE body for `AttendancePeriodLockDto`. Literal unchanged — this concern has no
    // renames; every field is name-for-name identical between the VM and the wire DTO.
    const mockLock: PeriodLockWire = {
      id: 'lock-1',
      periodStart: '2026-05-01',
      periodEnd: '2026-05-31',
      isLocked: true,
      lockedBy: 'hr-1',
      lockedAt: '2026-06-01T09:00:00Z',
      unlockedBy: null,
      unlockedAt: null,
    };

    it('getPayrollData GETs the feed with month + optional employeeIds csv (FR-1)', () => {
      const result: AttendancePayrollResultWire = {
        period: '2026-05',
        rows: [
          {
            employeeId: 'e1',
            period: '2026-05',
            totalWorkingDays: 22,
            totalPresentDays: 20,
            totalAbsentDays: 2,
            lopDays: 2,
            lateDeductionDays: 0.5,
            approvedOvertimeMinutes: 600,
            totalWorkMinutes: 9600,
            overtimeMultiplierDetails: { '1.5': 600 },
          },
        ],
      };
      let data: IAttendancePayrollResult | undefined;
      service.getPayrollData('2026-05', ['e1', 'e2']).subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/payroll-data` &&
          r.params.get('month') === '2026-05' &&
          r.params.get('employeeIds') === 'e1,e2',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(result);
      expect(data!.rows.length).toBe(1);
      expect(data!.rows[0].lopDays).toBe(2);
      // Loosely-typed jsonb passthrough (wire `object | null`, VM `unknown`). The shape is
      // owned by payroll and no attendance screen renders it, so coercing or reshaping it
      // here would be inventing structure. Pinned verbatim.
      expect(data!.rows[0].overtimeMultiplierDetails).toEqual({ '1.5': 600 });
      // Money (BR-4): 0 = no late-arrival day converted to LOP.
      expect(data!.rows[0].lateDeductionDays).toBe(0.5);
      expect(data!.rows[0].approvedOvertimeMinutes).toBe(600);
    });

    it('getPayrollData maps an empty body to an empty feed', () => {
      let data: IAttendancePayrollResult | undefined;
      service.getPayrollData('2026-05').subscribe((d) => (data = d));

      const empty: AttendancePayrollResultWire = {};
      httpMock.expectOne((r) => r.url === `${baseUrl}/payroll-data`).flush(empty);

      expect(data).toEqual({ period: '', rows: [] });
    });

    it('getPayrollData omits employeeIds when not provided', () => {
      service.getPayrollData('2026-05').subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/payroll-data` && r.params.get('month') === '2026-05',
      );
      expect(req.request.params.has('employeeIds')).toBeFalse();
      req.flush({ period: '2026-05', rows: [] });
    });

    it('getPeriodLock GETs the lock for the month and unwraps the envelope (FR-3)', () => {
      let data: IPeriodLock | null | undefined;
      service.getPeriodLock('2026-05').subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/period-lock` && r.params.get('month') === '2026-05',
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockLock);
      expect(data!.isLocked).toBeTrue();
    });

    /**
     * THE standing guard for the most dangerous edit in this slice. A `null` body means
     * the period was NEVER locked and it must stay `null`: running it through
     * `mapPeriodLock` would fabricate a lock row whose fail-closed `isLocked ?? true`
     * default reports an OPEN period as locked — hiding the "Lock Attendance" button and
     * leaving a dead "Unlock" behind it (the fabricated id would be '', so the confirm
     * handler silently returns). The service therefore uses
     * `map((res) => (res ? mapPeriodLock(res) : null))`, never `map(mapPeriodLock)`.
     */
    it('getPeriodLock returns null when no lock row exists', () => {
      // Seeded non-null so a mapper that fabricated a lock row would be caught.
      let data: IPeriodLock | null | undefined = {
        id: 'lock-1',
        periodStart: '2026-05-01',
        periodEnd: '2026-05-31',
        isLocked: true,
        lockedBy: 'hr-1',
        lockedAt: '2026-06-01T09:00:00Z',
        unlockedBy: null,
        unlockedAt: null,
      };
      service.getPeriodLock('2026-05').subscribe((d) => (data = d));
      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/period-lock` && r.params.get('month') === '2026-05',
      );
      req.flush(null);
      expect(data).toBeNull();
      // Not merely falsy: a fabricated `{ isLocked: true, id: '' }` would pass a bare
      // truthiness check, so assert the FIELD is unreachable too.
      expect(data?.isLocked).toBeUndefined();
    });

    it('getPeriodLock fails CLOSED on a lock row with no isLocked flag', () => {
      let data: IPeriodLock | null | undefined;
      service.getPeriodLock('2026-05').subscribe((d) => (data = d));
      const wire: PeriodLockWire = {
        id: 'lock-2',
        periodStart: '2026-05-01',
        periodEnd: '2026-05-31',
      };
      httpMock.expectOne((r) => r.url === `${baseUrl}/period-lock`).flush(wire);

      // `?? false` on a present-but-malformed lock row would tell HR the period is still
      // editable and that payroll must not pull yet — an assertion we cannot make from a
      // missing field. `?? true` at worst offers an Unlock the backend rejects visibly.
      // The genuinely-unlocked case is a null BODY, which the test above pins.
      expect(data!.isLocked).toBeTrue();
      expect(data!.lockedBy).toBeNull();
      expect(data!.lockedAt).toBeNull();
    });

    it('lockPeriod POSTs the date range and unwraps the lock (AC-4)', () => {
      let data: IPeriodLock | undefined;
      service.lockPeriod('2026-05-01', '2026-05-31').subscribe((d) => (data = d));

      const req = httpMock.expectOne(`${baseUrl}/period-lock`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        periodStart: '2026-05-01',
        periodEnd: '2026-05-31',
      });
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockLock);
      expect(data!.id).toBe('lock-1');
    });

    it('unlockPeriod POSTs to the unlock path (AC-5)', () => {
      let data: IPeriodLock | undefined;
      service.unlockPeriod('lock-1').subscribe((d) => (data = d));

      const req = httpMock.expectOne(`${baseUrl}/period-lock/lock-1/unlock`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...mockLock, isLocked: false });
      // Proves the fail-closed default does NOT blanket-force `true`: a present `false`
      // is honoured, so a genuinely unlocked period still shows "Lock Attendance".
      expect(data!.isLocked).toBeFalse();
    });

    it('getReconciliation GETs the reconciliation for the month (FR-5)', () => {
      const result: ReconciliationResultWire = {
        period: '2026-05',
        rows: [
          {
            employeeId: 'e1',
            employeeName: 'Ada Lovelace',
            presentDays: 20,
            lopDays: 2,
            approvedOvertimeMinutes: 600,
            totalWorkMinutes: 9600,
          },
        ],
      };
      let data: IReconciliationResult | undefined;
      service.getReconciliation('2026-05').subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/reconciliation` && r.params.get('month') === '2026-05',
      );
      expect(req.request.method).toBe('GET');
      req.flush(result);
      expect(data!.rows[0].employeeName).toBe('Ada Lovelace');
    });
  });

  describe('US-ATT-010 dashboard & reports', () => {
    it('getDashboardKpi GETs with date + scope and unwraps data (AC-1)', () => {
      const kpi: DashboardKpiWire = {
        date: '2026-06-15',
        expectedHeadcount: 50,
        clockedIn: 40,
        pendingClockIn: 5,
        onLeave: 3,
        absent: 2,
        attendancePercent: 80,
      };
      let data: IDashboardKpi | undefined;
      service.getDashboardKpi('2026-06-15', 'team').subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/dashboard` &&
          r.params.get('date') === '2026-06-15' &&
          r.params.get('scope') === 'team',
      );
      expect(req.request.method).toBe('GET');
      req.flush(kpi);
      expect(data!.clockedIn).toBe(40);
      expect(data!.expectedHeadcount).toBe(50);
      expect(data!.attendancePercent).toBe(80);
    });

    it('getDashboardKpi zeroes every counter on an empty body', () => {
      let data: IDashboardKpi | undefined;
      service.getDashboardKpi('2026-06-15').subscribe((d) => (data = d));

      const empty: DashboardKpiWire = {};
      httpMock.expectOne((r) => r.url === `${baseUrl}/dashboard`).flush(empty);

      // The five counters drive the KPI cards AND the donut arithmetic; the C# record
      // declares them non-nullable, so `?? 0` is degraded-payload defence, not a routine
      // path — but it must be 0, never an invented headcount.
      expect(data!.expectedHeadcount).toBe(0);
      expect(data!.clockedIn).toBe(0);
      expect(data!.pendingClockIn).toBe(0);
      expect(data!.onLeave).toBe(0);
      expect(data!.absent).toBe(0);
      expect(data!.attendancePercent).toBe(0);
      // Not rendered anywhere — the component tracks its own date() signal.
      expect(data!.date).toBe('');
    });

    it('getLiveBoard GETs the live board and unwraps rows (AC-2)', () => {
      const result: LiveBoardResultWire = {
        date: '2026-06-15',
        rows: [
          {
            employeeId: 'e1',
            employeeName: 'Ada Lovelace',
            status: 'CLOCKED_IN',
            clockInAt: '2026-06-15T08:05:00Z',
          },
        ],
      };
      let data: ILiveBoardResult | undefined;
      service.getLiveBoard('2026-06-15').subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/dashboard/live-board` &&
          r.params.get('scope') === 'all',
      );
      expect(req.request.method).toBe('GET');
      req.flush(result);
      expect(data!.rows[0].status).toBe('CLOCKED_IN');
      expect(data!.rows[0].clockInAt).toBe('2026-06-15T08:05:00Z');
    });

    it('getLiveBoard maps a null date and null rows to \'\' and []', () => {
      let data: ILiveBoardResult | undefined;
      service.getLiveBoard('2026-06-15').subscribe((d) => (data = d));

      const wire: LiveBoardResultWire = { date: null, rows: null };
      httpMock.expectOne((r) => r.url === `${baseUrl}/dashboard/live-board`).flush(wire);

      // `[]` renders the board's own empty state — the honest read of "no rows sent".
      expect(data!.rows).toEqual([]);
      expect(data!.date).toBe('');
    });

    it('getLiveBoard leaves an absent status BLANK rather than claiming NOT_CLOCKED_IN', () => {
      let data: ILiveBoardResult | undefined;
      service.getLiveBoard('2026-06-15').subscribe((d) => (data = d));

      const wire: LiveBoardResultWire = {
        date: '2026-06-15',
        rows: [{ employeeId: 'e1', employeeName: 'Ada Lovelace' }],
      };
      httpMock.expectOne((r) => r.url === `${baseUrl}/dashboard/live-board`).flush(wire);

      // DELIBERATELY not coerced: '' misses the STATUS_META lookup and renders a blank
      // label on a neutral-grey chip, which reads as "unknown". A confident
      // "Not Clocked In" is something HR would act on; anything green is unthinkable.
      expect(data!.rows[0].status).toBe('');
      // Optional on the VM AND nullable on the wire — keep undefined, do not invent ''.
      expect(data!.rows[0].employeeNumber).toBeUndefined();
      expect(data!.rows[0].departmentName).toBeUndefined();
      // Only read when status === 'CLOCKED_IN'; undefined renders '—'.
      expect(data!.rows[0].clockInAt).toBeUndefined();
    });

    it('getDepartmentComparison GETs by month and unwraps rows (AC-3)', () => {
      const result: DeptComparisonResultWire = {
        month: '2026-06',
        rows: [
          { departmentId: 'd1', departmentName: 'Engineering', attendanceRatePct: 95, employeeCount: 12 },
        ],
      };
      let data: IDeptComparisonResult | undefined;
      service.getDepartmentComparison('2026-06').subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/reports/department-comparison` &&
          r.params.get('month') === '2026-06',
      );
      expect(req.request.method).toBe('GET');
      req.flush(result);
      expect(data!.rows[0].attendanceRatePct).toBe(95);
    });

    it('getCustomReport GETs with from/to + filters and unwraps rows (AC-4)', () => {
      const result: CustomReportResultWire = {
        from: '2026-06-01',
        to: '2026-06-15',
        rows: [
          {
            employeeId: 'e1',
            employeeName: 'Ada Lovelace',
            presentDays: 10,
            absentDays: 1,
            lateCount: 2,
            overtimeMinutes: 120,
            workMinutes: 4800,
          },
        ],
      };
      let data: ICustomReportResult | undefined;
      service
        .getCustomReport({ from: '2026-06-01', to: '2026-06-15', departmentId: 'd1' })
        .subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/reports/custom` &&
          r.params.get('from') === '2026-06-01' &&
          r.params.get('to') === '2026-06-15' &&
          r.params.get('departmentId') === 'd1',
      );
      expect(req.request.method).toBe('GET');
      req.flush(result);
      expect(data!.rows[0].presentDays).toBe(10);
    });

    it('exportCustomReport GETs a blob with format + filters (AC-4, FR-5)', () => {
      let resp: import('@angular/common/http').HttpResponse<Blob> | undefined;
      service
        .exportCustomReport({ from: '2026-06-01', to: '2026-06-15' }, 'xlsx')
        .subscribe((r) => (resp = r));

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/reports/custom/export` &&
          r.params.get('format') === 'xlsx',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['data'], { type: 'application/octet-stream' }));
      expect(resp!.body).toBeInstanceOf(Blob);
    });

    it('getTrends GETs with months and unwraps the four series (AC-5)', () => {
      const result: TrendsResultWire = {
        attendanceRate: [{ period: '2026-06', value: 92 }],
        lateArrivals: [{ period: '2026-06', value: 3 }],
        overtimeHours: [{ period: '2026-06', value: 40 }],
        absenteeismRate: [{ period: '2026-06', value: 4 }],
      };
      let data: ITrendsResult | undefined;
      service.getTrends(12).subscribe((d) => (data = d));

      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/reports/trends` && r.params.get('months') === '12',
      );
      expect(req.request.method).toBe('GET');
      req.flush(result);
      expect(data!.attendanceRate[0].value).toBe(92);
      // The point fields are `period` / `value`. A wrong key here renders an EMPTY chart
      // with no error — the SVG just flat-lines at y = chartH.
      expect(data!.attendanceRate[0].period).toBe('2026-06');
      expect(data!.lateArrivals[0].value).toBe(3);
      expect(data!.overtimeHours[0].value).toBe(40);
      expect(data!.absenteeismRate[0].value).toBe(4);
    });

    it('getTrends maps an empty body to four empty series', () => {
      let data: ITrendsResult | undefined;
      service.getTrends().subscribe((d) => (data = d));

      const empty: TrendsResultWire = {};
      httpMock.expectOne((r) => r.url === `${baseUrl}/reports/trends`).flush(empty);

      // All FOUR series, not just the first: an empty series yields no points and an empty
      // chart card, which is what the data actually says.
      expect(data!.attendanceRate).toEqual([]);
      expect(data!.lateArrivals).toEqual([]);
      expect(data!.overtimeHours).toEqual([]);
      expect(data!.absenteeismRate).toEqual([]);
    });

    it('getScheduledReports GETs and unwraps the config list (FR-8)', () => {
      const list: ScheduledReportConfigWire[] = [
        {
          id: 's1',
          reportType: 'daily-attendance',
          frequency: 'DAILY',
          filters: {},
          // KNOWN DEFECT (FR-8 create): the wire types `recipients` as `uuid[]` and the C#
          // DTO binds `IReadOnlyList<Guid>`, but the scheduled-report form collects EMAIL
          // ADDRESSES — so the real POST 400s in production. This fixture keeps the emails
          // deliberately: swapping them for GUIDs would make a green test certify a flow
          // that is still broken end-to-end. Fix the form/backend, then this fixture.
          recipients: ['hr@acme.com'],
          deliveryTime: '08:00',
          format: 'CSV',
          isActive: true,
        },
      ];
      let data: IScheduledReportConfig[] | undefined;
      service.getScheduledReports().subscribe((d) => (data = d));

      const req = httpMock.expectOne(`${baseUrl}/reports/scheduled`);
      expect(req.request.method).toBe('GET');
      req.flush(list);
      expect(data!.length).toBe(1);
      expect(data![0].id).toBe('s1');
      expect(data![0].isActive).toBeTrue();
      expect(data![0].recipients).toEqual(['hr@acme.com']);
    });

    it('getScheduledReports never switches an absent isActive into a live schedule (FR-8)', () => {
      let data: IScheduledReportConfig[] | undefined;
      service.getScheduledReports().subscribe((d) => (data = d));

      const wire: ScheduledReportConfigWire[] = [{ reportType: 'x', frequency: 'DAILY' }];
      httpMock.expectOne(`${baseUrl}/reports/scheduled`).flush(wire);

      // `isActive` is the gate the Hangfire job filters on, so an ABSENT flag must read as
      // "Paused". Fails closed: the worst case is HR re-enabling a schedule that was
      // already on, never a schedule that keeps mailing while the UI says it is paused.
      expect(data![0].isActive).toBeFalse();
      // The create form refuses to submit on empty — the least-claiming default.
      expect(data![0].recipients).toEqual([]);
      // Absent on create; `undefined` keeps deleteSchedule()'s `if (!config.id) return;` honest.
      expect(data![0].id).toBeUndefined();
      // Rendered raw between two "·" separators — '' shows a visible gap rather than
      // asserting a delivery time the schedule does not have.
      expect(data![0].deliveryTime).toBe('');
      // Passed through with a guarded cast, never coerced to a union member: defaulting
      // frequency to 'DAILY' would tell HR a report arrives every morning when the
      // backend's IsDue() returns false for an unrecognised frequency and it never sends.
      expect(data![0].format).toBe('');
      expect(data![0].frequency).toBe('DAILY');
      expect(data![0].filters).toEqual({});
    });

    it('createScheduledReport POSTs the config and unwraps the created row (FR-8)', () => {
      const config = {
        reportType: 'daily-attendance',
        frequency: 'DAILY' as const,
        filters: {},
        // KNOWN DEFECT (FR-8 create): the backend expects user GUIDs
        // (`IReadOnlyList<Guid>`, wire `uuid[]`) but the form collects email addresses, so
        // this POST 400s in production. Left as emails on purpose — changing it to GUIDs
        // would certify a flow that is still broken end-to-end.
        recipients: ['hr@acme.com'],
        deliveryTime: '08:00',
        format: 'CSV' as const,
        isActive: true,
      };
      let data: { id?: string } | undefined;
      service.createScheduledReport(config).subscribe((d) => (data = d));

      const req = httpMock.expectOne(`${baseUrl}/reports/scheduled`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.reportType).toBe('daily-attendance');
      req.flush({ ...config, id: 's-new' });
      expect(data!.id).toBe('s-new');
    });

    it('deleteScheduledReport DELETEs by id (FR-8)', () => {
      let done = false;
      service.deleteScheduledReport('s1').subscribe(() => (done = true));

      const req = httpMock.expectOne(`${baseUrl}/reports/scheduled/s1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      expect(done).toBeTrue();
    });
  });
});

// ─── Pure error helpers (no TestBed / httpMock.verify conflicts) ──────────

describe('AttendanceService.parseError (pure function)', () => {
  it('should parse an already-clocked-in 409 body (AC-2)', () => {
    const err = {
      error: { message: 'You have already clocked in. Please clock out first.', code: 'already_clocked_in' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseError(err);
    expect(parsed!.message).toContain('already clocked in');
    expect(parsed!.code).toBe('already_clocked_in');
  });

  it('should parse an IP-not-allowed 403 body (AC-5)', () => {
    const err = {
      error: { message: 'Clock-in is only allowed from authorized network locations.', code: 'ip_not_allowed' },
    } as HttpErrorResponse;
    expect(AttendanceService.parseError(err)!.code).toBe('ip_not_allowed');
  });

  it('should return null for a non-object error body', () => {
    const err = { error: 'boom' } as HttpErrorResponse;
    expect(AttendanceService.parseError(err)).toBeNull();
  });

  it('parseErrorMessage should extract the message', () => {
    const err = { error: { message: 'Outside geo-fence', code: 'geo_fence_violation' } } as HttpErrorResponse;
    expect(AttendanceService.parseErrorMessage(err)).toBe('Outside geo-fence');
  });

  it('parseErrorMessage should fall back for an unknown shape', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(AttendanceService.parseErrorMessage(err)).toBe('An unexpected error occurred.');
  });

  it('parseRegularizationError should parse a lookback rejection (AC-3)', () => {
    const err = {
      error: { message: 'Regularization requests can only be submitted for the last 7 days.', code: 'lookback_exceeded' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseRegularizationError(err);
    expect(parsed!.message).toContain('last 7 days');
    expect(parsed!.code).toBe('lookback_exceeded');
  });

  it('parseRegularizationError should return null for a non-object body', () => {
    const err = { error: 'boom' } as HttpErrorResponse;
    expect(AttendanceService.parseRegularizationError(err)).toBeNull();
  });

  it('parseShiftInUseError parses the 409 in-use body (AC-4)', () => {
    const err = {
      error: { message: 'This shift is assigned to 3 employees. Please reassign them before deleting.', code: 'shift_in_use' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseShiftInUseError(err);
    expect(parsed!.message).toContain('assigned to 3 employees');
    expect(parsed!.code).toBe('shift_in_use');
  });

  it('parseShiftInUseError returns null for a non-object body', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(AttendanceService.parseShiftInUseError(err)).toBeNull();
  });
});

// ─── US-ATT-009 pure helpers ──────────────────────────────────────────────

describe('US-ATT-009 period helpers (pure function)', () => {
  it('formatPeriodLabel formats a yyyy-MM as a month-year label', () => {
    expect(formatPeriodLabel('2026-05')).toContain('2026');
    expect(formatPeriodLabel('2026-05')).toContain('May');
  });

  it('formatPeriodLabel falls back to the raw input for a bad shape', () => {
    expect(formatPeriodLabel('nope')).toBe('nope');
  });

  it('periodDateRange returns the first and last day of the month', () => {
    expect(periodDateRange('2026-02')).toEqual({ start: '2026-02-01', end: '2026-02-28' });
    expect(periodDateRange('2026-05')).toEqual({ start: '2026-05-01', end: '2026-05-31' });
  });
});
