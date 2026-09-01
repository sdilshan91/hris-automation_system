import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
  HttpResponse,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAttendanceLog,
  IClockInRequest,
  IClockStatus,
  IClockInErrorResponse,
  IClockOutRequest,
  IClockOutResult,
  ICreateRegularizationRequest,
  IRegularization,
  IRegularizationErrorResponse,
  IPendingRegularization,
  IPendingRegularizationQuery,
  IApproveRegularizationRequest,
  IRejectRegularizationRequest,
  IRegularizationDecisionDto,
  IBulkApproveRequest,
  IBulkApproveResult,
  IRegularizationActionErrorResponse,
  RegularizationAction,
  IShift,
  IShiftRequest,
  IShiftAssignmentRequest,
  IAssignmentResult,
  IResolvedShift,
  IShiftInUseErrorResponse,
  IOvertime,
  IOvertimePreApprovalRequest,
  IOvertimeQueueItem,
  IOvertimeApproveRequest,
  IOvertimeRejectRequest,
  IOvertimeDecision,
  IOvertimeReportResult,
  IOvertimeActionErrorResponse,
  IMonthlySummaryQuery,
  IMonthlySummaryResult,
  IEmployeeDailyBreakdownResult,
  ISummaryGenerationStatus,
  SummaryExportFormat,
  ILatePolicy,
  ILateEarlyReportQuery,
  ILateEarlyReportResult,
  ILatenessScore,
  IAttendancePayrollResult,
  IPeriodLock,
  IReconciliationResult,
  AttendanceScope,
  IDashboardKpi,
  ILiveBoardResult,
  IDeptComparisonResult,
  ICustomReportFilters,
  ICustomReportResult,
  CustomReportExportFormat,
  ITrendsResult,
  IScheduledReportConfig,
  // ── D1 slice 3: generated wire contract + the explicit mappers to the view models. Every
  //    every request below used to name a hand-written `I…` type — an unchecked CAST, not a check,
  //    so the compiler accepted whatever the server sent. These `…Wire` aliases are derived from
  //    the API's own OpenAPI document, so a DTO rename is now a compile error here instead of an
  //    `undefined` on screen. See the mapper block at the foot of attendance.models.ts.
  ClockStatusWire,
  AttendanceLogWire,
  ClockOutResultWire,
  MonthlySummaryResultWire,
  EmployeeDailyBreakdownResultWire,
  SummaryGenerationStatusWire,
  mapClockStatus,
  mapAttendanceLog,
  mapClockOutResult,
  mapMonthlySummaryResult,
  mapEmployeeDailyBreakdownResult,
  mapSummaryGenerationStatus,
  RegularizationWire,
  PendingRegularizationQueueWire,
  RegularizationDecisionWire,
  BulkApproveRegularizationWire,
  mapRegularization,
  mapPendingRegularization,
  mapRegularizationDecision,
  mapBulkApproveResult,
  ShiftWire,
  ResolvedShiftWire,
  AssignmentResultWire,
  mapShift,
  mapResolvedShift,
  mapAssignmentResult,
  OvertimeWire,
  OvertimeQueueResultWire,
  OvertimeDecisionWire,
  OvertimeReportResultWire,
  mapOvertime,
  mapOvertimeList,
  mapOvertimeQueue,
  mapOvertimeDecision,
  mapOvertimeReportResult,
  LatePolicyWire,
  LateEarlyReportResultWire,
  LatenessScoreWire,
  AttendancePayrollResultWire,
  PeriodLockWire,
  ReconciliationResultWire,
  mapLatePolicy,
  mapLateEarlyReportResult,
  mapLatenessScore,
  mapAttendancePayrollResult,
  mapPeriodLock,
  mapReconciliationResult,
  DashboardKpiWire,
  LiveBoardResultWire,
  DeptComparisonResultWire,
  CustomReportResultWire,
  TrendsResultWire,
  ScheduledReportConfigWire,
  mapDashboardKpi,
  mapLiveBoardResult,
  mapDeptComparisonResult,
  mapCustomReportResult,
  mapTrendsResult,
  mapScheduledReportConfig,
  toScheduledReportConfigWire,
} from '../models/attendance.models';

/**
 * US-ATT-001: Service for the employee's self clock-in + today's clock status.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The
 * backend stamps tenant_id, IP, and user-agent server-side (FR-1, FR-5) — the
 * FE never sends them.
 *
 * Backend endpoints (assumed contract -- backend agent building in parallel):
 *   GET  /api/v1/attendance/status     - current employee's clock-in status today (IClockStatus)
 *   POST /api/v1/attendance/clock-in   - create a clock-in; returns IAttendanceLog
 *   POST /api/v1/attendance/clock-out  - close the open record; returns IClockOutResult (US-ATT-002)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is `${apiBaseUrl}/attendance`.
 */
@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/attendance`;

  // --- Read --------------------------------------------------

  /**
   * Get the current employee's clock-in status for today (FR-6, §8).
   * Used to initialise the card: already-clocked-in -> live timer (AC-2 reflect),
   * `requireGeolocation` -> AC-3 vs AC-4 branch, shift name/start -> context.
   */
  getStatus(): Observable<IClockStatus> {
    return this.http
      .get<ClockStatusWire>(`${this.baseUrl}/status`, {
        withCredentials: true,
      })
      .pipe(map(mapClockStatus));
  }

  // --- Write -------------------------------------------------

  /** Submit a clock-in (FR-1, AC-1). Returns the created attendance log. */
  clockIn(request: IClockInRequest): Observable<IAttendanceLog> {
    return this.http
      .post<AttendanceLogWire>(`${this.baseUrl}/clock-in`, request, {
        withCredentials: true,
      })
      .pipe(map(mapAttendanceLog));
  }

  /**
   * US-ATT-002: Close the open attendance record (FR-1, AC-1). The backend sets
   * clock_out to the server UTC time (§10 — never client-reported), computes total
   * work minutes / overtime / status, and returns them in IClockOutResult.
   * AC-5: coordinates are included only when the tenant geo policy requires them.
   */
  clockOut(request: IClockOutRequest): Observable<IClockOutResult> {
    return this.http
      .post<ClockOutResultWire>(`${this.baseUrl}/clock-out`, request, {
        withCredentials: true,
      })
      .pipe(map(mapClockOutResult));
  }

  // --- US-ATT-003: Regularization ----------------------------

  /**
   * US-ATT-003 (FR-1, FR-2, AC-1/AC-2): submit an attendance regularization
   * request. Returns the created record with status 'PENDING'. Backend rejections
   * (AC-3 lookback, AC-4 duplicate pending, AC-5 locked payroll period) arrive as a
   * `{ message, code }` body — the caller displays the message verbatim.
   */
  submitRegularization(
    request: ICreateRegularizationRequest,
  ): Observable<IRegularization> {
    return this.http
      .post<RegularizationWire>(
        `${this.baseUrl}/regularizations`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapRegularization));
  }

  /**
   * US-ATT-003 (§8): list the current employee's regularization requests with
   * their status, most-recent first (ordering owned by the backend). Tenant-scoped
   * via the tenantInterceptor; the employee is resolved from the JWT server-side.
   */
  listRegularizations(): Observable<IRegularization[]> {
    return this.http
      .get<RegularizationWire[]>(`${this.baseUrl}/regularizations`, {
        withCredentials: true,
      })
      .pipe(map((rows) => (rows ?? []).map(mapRegularization)));
  }

  // --- US-ATT-004: Manager approval queue -------------------

  /**
   * US-ATT-004 (FR-1, AC-3) REAL contract: list the pending regularization requests
   * for the authenticated manager's direct reports.
   *   GET /api/v1/attendance/regularizations/pending  (optional employeeId/fromDate/toDate)
   *   -> ApiResponse<PendingRegularizationQueueResult> { items, totalCount }
   * The backend scopes by manager + tenant server-side (FR-7, NFR-3). Reads `data.items`.
   */
  getPendingApprovals(
    query?: IPendingRegularizationQuery,
  ): Observable<IPendingRegularization[]> {
    let params = new HttpParams();
    if (query?.employeeId) {
      params = params.set('employeeId', query.employeeId);
    }
    if (query?.fromDate) {
      params = params.set('fromDate', query.fromDate);
    }
    if (query?.toDate) {
      params = params.set('toDate', query.toDate);
    }
    return this.http
      .get<PendingRegularizationQueueWire>(
        `${this.baseUrl}/regularizations/pending`,
        { withCredentials: true, params },
      )
      .pipe(map((res) => (res?.items ?? []).map(mapPendingRegularization)));
  }

  /**
   * US-ATT-004 (AC-1, AC-2): approve or reject a single regularization request. Kept
   * as the single signature the component calls; internally routes to the REAL
   * PATH-based endpoints (approve vs reject). For REJECT the `comment` arg carries the
   * mandatory reason (min 10 chars, enforced by the caller). Backend denials (AC-5
   * authorization, BR-5 payroll lock) arrive as a `{ message, code }` body the caller
   * displays verbatim. Unwraps the ApiResponse<T> envelope to the decision DTO.
   */
  processRegularization(
    regularizationId: string,
    action: RegularizationAction,
    comment?: string,
  ): Observable<IRegularizationDecisionDto> {
    return action === 'REJECT'
      ? this.rejectRegularization(regularizationId, comment ?? '')
      : this.approveRegularization(regularizationId, comment);
  }

  /**
   * US-ATT-004 REAL contract: approve a single request.
   *   POST /api/v1/attendance/regularizations/{id}/approve  body { comment? }
   */
  approveRegularization(
    regularizationId: string,
    comment?: string,
  ): Observable<IRegularizationDecisionDto> {
    const body: IApproveRegularizationRequest = comment ? { comment } : {};
    return this.http
      .post<RegularizationDecisionWire>(
        `${this.baseUrl}/regularizations/${regularizationId}/approve`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapRegularizationDecision));
  }

  /**
   * US-ATT-004 REAL contract: reject a single request. The body field is `reason`
   * (NOT `comment`), required min 10 chars (BR-1) — enforced by the caller.
   *   POST /api/v1/attendance/regularizations/{id}/reject  body { reason }
   */
  rejectRegularization(
    regularizationId: string,
    reason: string,
  ): Observable<IRegularizationDecisionDto> {
    const body: IRejectRegularizationRequest = { reason };
    return this.http
      .post<RegularizationDecisionWire>(
        `${this.baseUrl}/regularizations/${regularizationId}/reject`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapRegularizationDecision));
  }

  /**
   * US-ATT-004 (BR-7) REAL contract: approve multiple regularization requests in one
   * call. The backend processes each id independently and returns a per-item result
   * (`items[].succeeded`) so a partial failure (one locked period, AC-5/BR-5) does not
   * roll back the rest.
   *   POST /api/v1/attendance/regularizations/bulk-approve  body { regularizationIds, comment? }
   * Unwraps the ApiResponse<T> envelope.
   */
  bulkApprove(ids: string[], comment?: string): Observable<IBulkApproveResult> {
    const body: IBulkApproveRequest = comment
      ? { regularizationIds: ids, comment }
      : { regularizationIds: ids };
    return this.http
      .post<BulkApproveRegularizationWire>(
        `${this.baseUrl}/regularizations/bulk-approve`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapBulkApproveResult));
  }

  /**
   * US-ATT-004 (AC-5, BR-5): parse an approve/reject/bulk error body into the typed
   * shape. The component shows `message` verbatim.
   */
  static parseActionError(
    err: HttpErrorResponse,
  ): IRegularizationActionErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IRegularizationActionErrorResponse;
    }
    return null;
  }

  /** Parse a regularization error body (AC-3/AC-4/AC-5); shape matches clock-in. */
  static parseRegularizationError(
    err: HttpErrorResponse,
  ): IRegularizationErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IRegularizationErrorResponse;
    }
    return null;
  }

  // --- US-ATT-005: Shift management & assignment -------------

  /**
   * US-ATT-005 (AC-1, §8): list all shift definitions for the tenant. Unwraps the
   * ApiResponse<T> envelope to ShiftDto[]. Tenant-scoped via the tenantInterceptor.
   *   GET /api/v1/attendance/shifts -> ApiResponse<ShiftDto[]>
   */
  getShifts(): Observable<IShift[]> {
    return this.http
      .get<ShiftWire[]>(`${this.baseUrl}/shifts`, {
        withCredentials: true,
      })
      .pipe(map((res) => (res ?? []).map(mapShift)));
  }

  /**
   * US-ATT-005 (AC-1, FR-2): create a new shift definition. The backend stamps
   * tenant_id + audit fields server-side (NFR-3) and returns the created ShiftDto.
   *   POST /api/v1/attendance/shifts  body ShiftRequest -> ApiResponse<ShiftDto>
   */
  createShift(request: IShiftRequest): Observable<IShift> {
    return this.http
      .post<ShiftWire>(`${this.baseUrl}/shifts`, request, {
        withCredentials: true,
      })
      .pipe(map(mapShift));
  }

  /**
   * US-ATT-005 (FR-2): update an existing shift definition.
   *   PUT /api/v1/attendance/shifts/{id}  body ShiftRequest -> ApiResponse<ShiftDto>
   */
  updateShift(id: string, request: IShiftRequest): Observable<IShift> {
    return this.http
      .put<ShiftWire>(`${this.baseUrl}/shifts/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapShift));
  }

  /**
   * US-ATT-005 (AC-4, FR-6): delete a shift. Returns 204 on success. When the shift
   * has active assignments the backend returns 409 `{ message, code: 'shift_in_use' }`
   * — the caller shows `message` verbatim (see {@link parseShiftInUseError}).
   *   DELETE /api/v1/attendance/shifts/{id} -> 204 | 409
   */
  deleteShift(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/shifts/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * US-ATT-005 (FR-8): clone an existing shift into a new variant. The backend copies
   * the definition (un-defaulted, with a derived name) and returns the new ShiftDto.
   *   POST /api/v1/attendance/shifts/{id}/clone -> ApiResponse<ShiftDto>
   */
  cloneShift(id: string): Observable<IShift> {
    return this.http
      .post<ShiftWire>(`${this.baseUrl}/shifts/${id}/clone`, {}, {
        withCredentials: true,
      })
      .pipe(map(mapShift));
  }

  /**
   * US-ATT-005 (AC-2, FR-3): bulk-assign a shift to employees with an effective date.
   * The backend handles effective-dating + non-overlap (AC-3, BR-2/BR-3) and returns
   * the assigned count. The FE shows `assignedCount` in the success toast.
   *   POST /api/v1/attendance/shifts/{id}/assign
   *     body { employeeIds, effectiveFrom } -> ApiResponse<{ assignedCount, employeeShiftIds }>
   */
  assignShift(
    id: string,
    request: IShiftAssignmentRequest,
  ): Observable<IAssignmentResult> {
    return this.http
      .post<AssignmentResultWire>(
        `${this.baseUrl}/shifts/${id}/assign`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapAssignmentResult));
  }

  /**
   * US-ATT-005 (FR-7, AC-5): resolve the shift applicable to an employee on a date —
   * for ROTATING shifts the backend computes the right step. Used by the optional
   * employee-profile current-shift card.
   *   GET /api/v1/attendance/employees/{employeeId}/shift?date=yyyy-MM-dd
   *     -> ApiResponse<ResolvedShiftDto>
   */
  getResolvedShift(employeeId: string, date: string): Observable<IResolvedShift> {
    const params = new HttpParams().set('date', date);
    return this.http
      .get<ResolvedShiftWire>(
        `${this.baseUrl}/employees/${employeeId}/shift`,
        { withCredentials: true, params },
      )
      .pipe(map(mapResolvedShift));
  }

  /**
   * US-ATT-005 (AC-4): parse the 409 shift-in-use error body. The component shows
   * `message` verbatim ("This shift is assigned to {N} employees...").
   */
  static parseShiftInUseError(
    err: HttpErrorResponse,
  ): IShiftInUseErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IShiftInUseErrorResponse;
    }
    return null;
  }

  // --- US-ATT-006: Overtime tracking & approval --------------

  /**
   * US-ATT-006 (AC-2, FR-4): submit an overtime pre-approval request. Returns the
   * created record with type PRE_APPROVED. Tenant + employee resolved server-side.
   *   POST /api/v1/attendance/overtime/pre-approval  body { date, expectedHours, reason }
   *     -> ApiResponse<OvertimeDto>
   */
  submitOvertimePreApproval(
    request: IOvertimePreApprovalRequest,
  ): Observable<IOvertime> {
    return this.http
      .post<OvertimeWire>(
        `${this.baseUrl}/overtime/pre-approval`,
        request,
        { withCredentials: true },
      )
      .pipe(map(mapOvertime));
  }

  /**
   * US-ATT-006 (§8): list the current employee's overtime records (auto-detected +
   * pre-approved), most-recent first (ordering owned by the backend). Feeds the daily
   * card overtime detail and the weekly-progress bar.
   *   GET /api/v1/attendance/overtime/my -> ApiResponse<OvertimeDto[]>
   */
  getMyOvertime(): Observable<IOvertime[]> {
    return this.http
      .get<OvertimeWire[]>(`${this.baseUrl}/overtime/my`, {
        withCredentials: true,
      })
      .pipe(map(mapOvertimeList));
  }

  /**
   * US-ATT-006 (AC-3): list the pending overtime records for the authenticated
   * manager's team. Backend scopes by manager + tenant server-side (BR-8, NFR-2).
   *   GET /api/v1/attendance/overtime/pending
   *     -> ApiResponse<{ items: OvertimeQueueItemDto[], totalCount }>  (reads data.items)
   */
  getPendingOvertime(): Observable<IOvertimeQueueItem[]> {
    return this.http
      .get<OvertimeQueueResultWire>(
        `${this.baseUrl}/overtime/pending`,
        { withCredentials: true },
      )
      .pipe(map(mapOvertimeQueue));
  }

  /**
   * US-ATT-006 (FR-6, AC-4): approve an overtime record, optionally adjusting the
   * awarded minutes (FR-6) and adding a comment. Self-approval (BR-8) / not-team-member
   * arrive as 403 `{ message, code }`; already-decided as 409 — shown verbatim.
   *   POST /api/v1/attendance/overtime/{id}/approve  body { approvedMinutes?, comment? }
   *     -> ApiResponse<OvertimeDecisionDto>
   */
  approveOvertime(
    id: string,
    approvedMinutes?: number,
    comment?: string,
  ): Observable<IOvertimeDecision> {
    const body: IOvertimeApproveRequest = {};
    if (approvedMinutes != null) {
      body.approvedMinutes = approvedMinutes;
    }
    if (comment) {
      body.comment = comment;
    }
    return this.http
      .post<OvertimeDecisionWire>(
        `${this.baseUrl}/overtime/${id}/approve`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapOvertimeDecision));
  }

  /**
   * US-ATT-006: reject an overtime record. `reason` is required, min 10 chars
   * (enforced by the caller).
   *   POST /api/v1/attendance/overtime/{id}/reject  body { reason } -> ApiResponse<OvertimeDecisionDto>
   */
  rejectOvertime(id: string, reason: string): Observable<IOvertimeDecision> {
    const body: IOvertimeRejectRequest = { reason };
    return this.http
      .post<OvertimeDecisionWire>(
        `${this.baseUrl}/overtime/${id}/reject`,
        body,
        { withCredentials: true },
      )
      .pipe(map(mapOvertimeDecision));
  }

  /**
   * US-ATT-006 (AC-5): the monthly overtime report for HR — approved/pending/rejected
   * minutes and record count per employee for the selected month.
   *   GET /api/v1/attendance/overtime/report?month=yyyy-MM
   *     -> ApiResponse<OvertimeReportResult>
   */
  getOvertimeReport(month: string): Observable<IOvertimeReportResult> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<OvertimeReportResultWire>(
        `${this.baseUrl}/overtime/report`,
        { withCredentials: true, params },
      )
      .pipe(map(mapOvertimeReportResult));
  }

  /**
   * US-ATT-006 (AC-4, BR-8): parse an overtime approve/reject error body into the typed
   * shape (self_approval / not_team_member / already_actioned). Shows `message` verbatim.
   */
  static parseOvertimeActionError(
    err: HttpErrorResponse,
  ): IOvertimeActionErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IOvertimeActionErrorResponse;
    }
    return null;
  }

  // --- US-ATT-007: Monthly attendance summary ----------------

  /**
   * US-ATT-007 (AC-1, AC-5, FR-5): the monthly attendance summary for the tenant — one
   * row per employee for the selected month, with optional department/location/shift/
   * status filters. Unwraps the ApiResponse<T> envelope to MonthlySummaryResult.
   *   GET /api/v1/attendance/summary/monthly?month=yyyy-MM&departmentId=&locationId=&shiftId=&status=
   *     -> ApiResponse<MonthlySummaryResult>
   */
  getMonthlySummary(
    query: IMonthlySummaryQuery,
  ): Observable<IMonthlySummaryResult> {
    let params = new HttpParams().set('month', query.month);
    if (query.departmentId) {
      params = params.set('departmentId', query.departmentId);
    }
    if (query.locationId) {
      params = params.set('locationId', query.locationId);
    }
    if (query.shiftId) {
      params = params.set('shiftId', query.shiftId);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    return this.http
      .get<MonthlySummaryResultWire>(
        `${this.baseUrl}/summary/monthly`,
        { withCredentials: true, params },
      )
      .pipe(map(mapMonthlySummaryResult));
  }

  /**
   * US-ATT-007 (AC-2): the day-by-day attendance breakdown for one employee in the
   * selected month — the drill-down behind a summary row.
   *   GET /api/v1/attendance/summary/monthly/{employeeId}?month=yyyy-MM
   *     -> ApiResponse<EmployeeDailyBreakdownResult>
   */
  getEmployeeDailyBreakdown(
    employeeId: string,
    month: string,
  ): Observable<IEmployeeDailyBreakdownResult> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<EmployeeDailyBreakdownResultWire>(
        `${this.baseUrl}/summary/monthly/${employeeId}`,
        { withCredentials: true, params },
      )
      .pipe(map(mapEmployeeDailyBreakdownResult));
  }

  /**
   * US-ATT-007 (AC-3): trigger on-demand generation of the month's summary via the
   * backend Hangfire job. Returns the current generation status; the caller polls
   * (re-invokes) until status === 'COMPLETED'.
   *   POST /api/v1/attendance/summary/monthly/generate?month=yyyy-MM
   *     -> ApiResponse<SummaryGenerationStatusDto>
   */
  generateMonthlySummary(month: string): Observable<ISummaryGenerationStatus> {
    const params = new HttpParams().set('month', month);
    return this.http
      .post<SummaryGenerationStatusWire>(
        `${this.baseUrl}/summary/monthly/generate`,
        {},
        { withCredentials: true, params },
      )
      .pipe(map(mapSummaryGenerationStatus));
  }

  /**
   * US-ATT-007 (AC-4, FR-6): export the monthly summary in CSV / Excel / PDF, honouring
   * the active department/location/shift filters. Returns the raw file blob; the caller
   * derives the filename from Content-Disposition or constructs one.
   *   GET /api/v1/attendance/summary/monthly/export?month=yyyy-MM&format=csv|xlsx|pdf&...
   *     -> file download (blob)
   */
  exportMonthlySummary(
    query: IMonthlySummaryQuery,
    format: SummaryExportFormat,
  ): Observable<HttpResponse<Blob>> {
    let params = new HttpParams()
      .set('month', query.month)
      .set('format', format);
    if (query.departmentId) {
      params = params.set('departmentId', query.departmentId);
    }
    if (query.locationId) {
      params = params.set('locationId', query.locationId);
    }
    if (query.shiftId) {
      params = params.set('shiftId', query.shiftId);
    }
    return this.http.get(`${this.baseUrl}/summary/monthly/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
      observe: 'response',
    });
  }

  // --- US-ATT-008: Late arrival & early departure ------------

  /**
   * US-ATT-008 (AC-4, FR-4): read the tenant's late-arrival policy. Unwraps the
   * ApiResponse<T> envelope to LatePolicyDto. Tenant-scoped via the tenantInterceptor.
   *   GET /api/v1/attendance/late-policy -> ApiResponse<LatePolicyDto>
   */
  getLatePolicy(): Observable<ILatePolicy> {
    return this.http
      .get<LatePolicyWire>(`${this.baseUrl}/late-policy`, {
        withCredentials: true,
      })
      .pipe(map(mapLatePolicy));
  }

  /**
   * US-ATT-008 (AC-4, FR-4): update the tenant's late-arrival policy. The backend
   * stamps tenant_id + audit fields server-side and returns the saved policy.
   *   PUT /api/v1/attendance/late-policy  body LatePolicyDto -> ApiResponse<LatePolicyDto>
   */
  updateLatePolicy(policy: ILatePolicy): Observable<ILatePolicy> {
    // `policy satisfies LatePolicyWire` pins the REQUEST shape at compile time. This is the
    // only write in the module that ships a view model straight back as the body, so without
    // it a future DTO rename would break the PUT silently while the response type still passed.
    return this.http
      .put<LatePolicyWire>(`${this.baseUrl}/late-policy`, policy satisfies LatePolicyWire, {
        withCredentials: true,
      })
      .pipe(map(mapLatePolicy));
  }

  /**
   * US-ATT-008 (AC-5, FR-6): the late/early-departure report — per-employee late and
   * early-departure counts/minutes for the date range, scoped to the manager's team or
   * (HR) all employees. The backend enforces the scope authorization server-side.
   *   GET /api/v1/attendance/late-early/report?from=&to=&departmentId=&employeeId=&scope=
   *     -> ApiResponse<LateEarlyReportResult>
   */
  getLateEarlyReport(
    query: ILateEarlyReportQuery,
  ): Observable<ILateEarlyReportResult> {
    let params = new HttpParams().set('from', query.from).set('to', query.to);
    if (query.departmentId) {
      params = params.set('departmentId', query.departmentId);
    }
    if (query.employeeId) {
      params = params.set('employeeId', query.employeeId);
    }
    if (query.scope) {
      params = params.set('scope', query.scope);
    }
    return this.http
      .get<LateEarlyReportResultWire>(
        `${this.baseUrl}/late-early/report`,
        { withCredentials: true, params },
      )
      .pipe(map(mapLateEarlyReportResult));
  }

  /**
   * US-ATT-008 (§8, AC-4): the employee's monthly lateness score for the self-service
   * progress indicator. `allowedLates` mirrors the policy threshold.
   *   GET /api/v1/attendance/late-early/my-score?month=yyyy-MM
   *     -> ApiResponse<LatenessScoreDto>
   */
  getMyLatenessScore(month: string): Observable<ILatenessScore> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<LatenessScoreWire>(
        `${this.baseUrl}/late-early/my-score`,
        { withCredentials: true, params },
      )
      .pipe(map(mapLatenessScore));
  }

  // --- US-ATT-009: Attendance integration with payroll -------

  /**
   * US-ATT-009 (FR-1, FR-2): the attendance-to-payroll feed — one summary row per
   * employee for the period (present/absent/LOP days, approved overtime minutes, work
   * minutes). Primarily consumed by the payroll module; exposed here for the HR preview.
   * Optional `employeeIds` scopes the pull to a subset (sent as a CSV query param).
   *   GET /api/v1/attendance/payroll-data?month=yyyy-MM&employeeIds=<csv>
   *     -> ApiResponse<{ period, rows: AttendancePayrollRowDto[] }>
   */
  getPayrollData(
    month: string,
    employeeIds?: string[],
  ): Observable<IAttendancePayrollResult> {
    let params = new HttpParams().set('month', month);
    if (employeeIds && employeeIds.length > 0) {
      params = params.set('employeeIds', employeeIds.join(','));
    }
    return this.http
      .get<AttendancePayrollResultWire>(
        `${this.baseUrl}/payroll-data`,
        { withCredentials: true, params },
      )
      .pipe(map(mapAttendancePayrollResult));
  }

  /**
   * US-ATT-009 (FR-3, AC-4): read the lock state of the attendance period covering the
   * given month. Returns `null` when the period has never been locked (no lock row).
   *   GET /api/v1/attendance/period-lock?month=yyyy-MM -> ApiResponse<PeriodLockDto | null>
   */
  getPeriodLock(month: string): Observable<IPeriodLock | null> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<PeriodLockWire | null>(
        `${this.baseUrl}/period-lock`,
        { withCredentials: true, params },
      )
      // A `null` body means this period has NEVER been locked, and it must stay null. Running
      // it through mapPeriodLock would fabricate a lock row whose fail-closed `isLocked ?? true`
      // default announces an open period as locked — which hides the "Lock Attendance" button
      // and leaves a dead "Unlock" in its place (the id would be ''). NOT map(mapPeriodLock).
      .pipe(map((res) => (res ? mapPeriodLock(res) : null)));
  }

  /**
   * US-ATT-009 (FR-3, AC-4, FR-4): lock the attendance period for a date range. The
   * backend records the locking HR officer + timestamp (FR-4) and freezes the range.
   *   POST /api/v1/attendance/period-lock  body { periodStart, periodEnd }
   *     -> ApiResponse<PeriodLockDto>
   */
  lockPeriod(periodStart: string, periodEnd: string): Observable<IPeriodLock> {
    return this.http
      .post<PeriodLockWire>(
        `${this.baseUrl}/period-lock`,
        { periodStart, periodEnd },
        { withCredentials: true },
      )
      .pipe(map(mapPeriodLock));
  }

  /**
   * US-ATT-009 (AC-5, FR-4): unlock a previously-locked period to allow corrections.
   * The backend records the unlocking HR officer + timestamp (FR-4) and re-opens the
   * range; payroll must re-pull (BR-6) once corrected and re-locked.
   *   POST /api/v1/attendance/period-lock/{id}/unlock -> ApiResponse<PeriodLockDto>
   */
  unlockPeriod(id: string): Observable<IPeriodLock> {
    return this.http
      .post<PeriodLockWire>(
        `${this.baseUrl}/period-lock/${id}/unlock`,
        {},
        { withCredentials: true },
      )
      .pipe(map(mapPeriodLock));
  }

  /**
   * US-ATT-009 (FR-5, AC-1): the reconciliation view — per-employee attendance summary
   * (present days, LOP, approved overtime, work minutes) for the period, to be compared
   * side-by-side with payroll inputs.
   *   GET /api/v1/attendance/reconciliation?month=yyyy-MM
   *     -> ApiResponse<{ period, rows: ReconciliationRowDto[] }>
   */
  getReconciliation(month: string): Observable<IReconciliationResult> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<ReconciliationResultWire>(
        `${this.baseUrl}/reconciliation`,
        { withCredentials: true, params },
      )
      .pipe(map(mapReconciliationResult));
  }

  // --- US-ATT-010: HR Dashboard & Reports --------------------

  /**
   * US-ATT-010 (AC-1, FR-1): today's attendance KPIs for the dashboard widget cards.
   * `scope=team` (BR-4) scopes a manager to their team; HR uses `all`. Polled every
   * ~30s (§10 SignalR fallback). Unwraps the ApiResponse<T> envelope.
   *   GET /attendance/dashboard?date=yyyy-MM-dd&scope=all|team -> ApiResponse<DashboardKpiDto>
   */
  getDashboardKpi(
    date: string,
    scope: AttendanceScope = 'all',
  ): Observable<IDashboardKpi> {
    const params = new HttpParams().set('date', date).set('scope', scope);
    return this.http
      .get<DashboardKpiWire>(`${this.baseUrl}/dashboard`, {
        withCredentials: true,
        params,
      })
      .pipe(map(mapDashboardKpi));
  }

  /**
   * US-ATT-010 (AC-2, FR-2): the live attendance board — every (in-scope) employee's
   * current status for the date. SignalR is unavailable (§10), so the component polls
   * this ~every 30s. Unwraps to the live-board result.
   *   GET /attendance/dashboard/live-board?date=yyyy-MM-dd&scope=all|team
   *     -> ApiResponse<{ date, rows: LiveBoardRowDto[] }>
   */
  getLiveBoard(
    date: string,
    scope: AttendanceScope = 'all',
  ): Observable<ILiveBoardResult> {
    const params = new HttpParams().set('date', date).set('scope', scope);
    return this.http
      .get<LiveBoardResultWire>(
        `${this.baseUrl}/dashboard/live-board`,
        { withCredentials: true, params },
      )
      .pipe(map(mapLiveBoardResult));
  }

  /**
   * US-ATT-010 (AC-3, FR-3): department attendance-rate comparison for the month.
   *   GET /attendance/reports/department-comparison?month=yyyy-MM
   *     -> ApiResponse<{ month, rows: DeptComparisonRowDto[] }>
   */
  getDepartmentComparison(month: string): Observable<IDeptComparisonResult> {
    const params = new HttpParams().set('month', month);
    return this.http
      .get<DeptComparisonResultWire>(
        `${this.baseUrl}/reports/department-comparison`,
        { withCredentials: true, params },
      )
      .pipe(map(mapDeptComparisonResult));
  }

  /**
   * US-ATT-010 (AC-4, FR-4): a custom date-range attendance report with optional
   * department/location/shift/status filters.
   *   GET /attendance/reports/custom?from&to&departmentId&locationId&shiftId&status
   *     -> ApiResponse<{ from, to, rows: CustomReportRowDto[] }>
   */
  getCustomReport(filters: ICustomReportFilters): Observable<ICustomReportResult> {
    return this.http
      .get<CustomReportResultWire>(
        `${this.baseUrl}/reports/custom`,
        {
          withCredentials: true,
          params: this.customReportParams(filters),
        },
      )
      .pipe(map(mapCustomReportResult));
  }

  /**
   * US-ATT-010 (AC-4, FR-5): export the custom report in CSV / Excel / PDF, honouring
   * the active filters. The backend generates the file (xlsx/pdf can't be built
   * client-side); returns the raw blob + headers so the caller derives the filename
   * from Content-Disposition.
   *   GET /attendance/reports/custom/export?from&to&format=csv|xlsx|pdf&... -> blob
   */
  exportCustomReport(
    filters: ICustomReportFilters,
    format: CustomReportExportFormat,
  ): Observable<HttpResponse<Blob>> {
    const params = this.customReportParams(filters).set('format', format);
    return this.http.get(`${this.baseUrl}/reports/custom/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /** Build the shared custom-report HttpParams (filters only). */
  private customReportParams(filters: ICustomReportFilters): HttpParams {
    let params = new HttpParams().set('from', filters.from).set('to', filters.to);
    if (filters.departmentId) {
      params = params.set('departmentId', filters.departmentId);
    }
    if (filters.locationId) {
      params = params.set('locationId', filters.locationId);
    }
    if (filters.shiftId) {
      params = params.set('shiftId', filters.shiftId);
    }
    if (filters.status) {
      params = params.set('status', filters.status);
    }
    return params;
  }

  /**
   * US-ATT-010 (AC-5, FR-6): the four 12-month trend series (attendance rate, late
   * arrivals, overtime hours, absenteeism rate). `months` defaults to 12 (BR-5: from
   * the monthly-summary table).
   *   GET /attendance/reports/trends?months=12 -> ApiResponse<TrendsResult>
   */
  getTrends(months = 12): Observable<ITrendsResult> {
    const params = new HttpParams().set('months', months.toString());
    return this.http
      .get<TrendsResultWire>(
        `${this.baseUrl}/reports/trends`,
        { withCredentials: true, params },
      )
      .pipe(map(mapTrendsResult));
  }

  /**
   * US-ATT-010 (FR-8): list the tenant's scheduled-report configurations.
   *   GET /attendance/reports/scheduled -> ApiResponse<ScheduledReportConfigDto[]>
   */
  getScheduledReports(): Observable<IScheduledReportConfig[]> {
    return this.http
      .get<ScheduledReportConfigWire[]>(
        `${this.baseUrl}/reports/scheduled`,
        { withCredentials: true },
      )
      .pipe(map((res) => (res ?? []).map(mapScheduledReportConfig)));
  }

  /**
   * US-ATT-010 (FR-8): create a scheduled-report configuration.
   *   POST /attendance/reports/scheduled body ScheduledReportConfigDto
   *     -> ApiResponse<ScheduledReportConfigDto>
   */
  createScheduledReport(
    config: IScheduledReportConfig,
  ): Observable<IScheduledReportConfig> {
    return this.http
      .post<ScheduledReportConfigWire>(
        `${this.baseUrl}/reports/scheduled`,
        toScheduledReportConfigWire(config),
        { withCredentials: true },
      )
      .pipe(map(mapScheduledReportConfig));
  }

  /**
   * US-ATT-010 (FR-8): update an existing scheduled-report configuration.
   *   PUT /attendance/reports/scheduled/{id} body ScheduledReportConfigDto
   *     -> ApiResponse<ScheduledReportConfigDto>
   */
  updateScheduledReport(
    id: string,
    config: IScheduledReportConfig,
  ): Observable<IScheduledReportConfig> {
    return this.http
      .put<ScheduledReportConfigWire>(
        `${this.baseUrl}/reports/scheduled/${id}`,
        toScheduledReportConfigWire(config),
        { withCredentials: true },
      )
      .pipe(map(mapScheduledReportConfig));
  }

  /**
   * US-ATT-010 (FR-8): delete a scheduled-report configuration. Returns 204.
   *   DELETE /attendance/reports/scheduled/{id} -> 204
   */
  deleteScheduledReport(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/reports/scheduled/${id}`, {
      withCredentials: true,
    });
  }

  // --- Error helper ------------------------------------------

  /** Parse an error response into a typed clock-in error (AC-2, AC-5, FR-3). */
  static parseError(err: HttpErrorResponse): IClockInErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IClockInErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from a clock-in error. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return AttendanceService.parseError(err)?.message ?? 'An unexpected error occurred.';
  }
}
