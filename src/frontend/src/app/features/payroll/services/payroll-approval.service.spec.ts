import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayrollApprovalService } from './payroll-approval.service';
import { environment } from '../../../../environments/environment';
import { IPayrollRun } from '../models/payroll-run.models';
import {
  APPROVAL_ACTION_LABELS,
  ApprovalHistoryWire,
  ApprovalResultWire,
  ApprovalSummaryWire,
  IApprovalHistoryEntry,
  IApprovalSummary,
  PendingApprovalWire,
} from '../models/approval.models';

describe('PayrollApprovalService (US-PAY-008)', () => {
  let service: PayrollApprovalService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  /**
   * D1 wire-types slice: every flushed body below is now the WIRE shape from
   * contracts/openapi/hrm-v1.json, not the view-model.
   *
   * The five action endpoints return `PayrollApprovalResultDto` — a result, NOT a run. The old spec
   * flushed a full `IPayrollRun` for all five, which is a payload the server has never produced; that
   * is precisely why `result.id === undefined` stayed green.
   */
  const wireActionResult: ApprovalResultWire = {
    runId: 'r-1',
    status: 'AwaitingApproval',
    action: 'Submitted',
    currentApprovalStep: 1,
    totalApprovalSteps: 2,
    workflowInstanceId: 'w-1',
  };

  /** `PayrollApprovalSummaryDto` — note `exceptions` is a STRING ARRAY on the wire. */
  const wireSummary: ApprovalSummaryWire = {
    runId: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'AwaitingApproval',
    totalEmployees: 250,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalStatutory: 120000,
    totalNet: 800000,
    previousMonthTotalNet: 760000,
    variancePercentage: 5.26,
    exceptions: ['3 payslip(s) have a negative net salary.'],
  };

  /** What `mapApprovalSummary(wireSummary)` must produce. */
  const mappedSummary: IApprovalSummary = {
    runId: 'r-1',
    totalEmployees: 250,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalStatutory: 120000,
    totalNet: 800000,
    previousMonthTotalNet: 760000,
    variancePercentage: 5.26,
    exceptions: [
      {
        severity: 'Warning',
        message: '3 payslip(s) have a negative net salary.',
        employeeName: null,
      },
    ],
  };

  /** `PayrollApprovalHistoryDto` — carries `actorUserId`, never an actor NAME. */
  const wireHistory: ApprovalHistoryWire[] = [
    {
      id: 'h-1',
      payrollRunId: 'r-1',
      workflowInstanceId: 'w-1',
      stepNumber: 1,
      action: 'Submitted',
      actorUserId: '22222222-2222-2222-2222-222222222222',
      comments: null,
      actedAt: '2026-05-31T11:00:00Z',
      ipAddress: '10.0.0.1',
    },
  ];

  /** What `mapApprovalHistoryEntry` must produce for `wireHistory[0]`. */
  const mappedHistory: IApprovalHistoryEntry[] = [
    {
      id: 'h-1',
      stepNumber: 1,
      action: 'Submitted',
      // No wire source — the DTO has only actorUserId. Must NOT be a GUID.
      actorName: null,
      comments: null,
      actedAt: '2026-05-31T11:00:00Z',
      ipAddress: '10.0.0.1',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayrollApprovalService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayrollApprovalService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── submit (AC-1) ────────────────────────────────────────

  it('submit() POSTs to runs/:id/submit-for-approval and returns the updated run', () => {
    let result: IPayrollRun | undefined;
    service.submit('r-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/submit-for-approval`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toBeNull();
    req.flush(wireActionResult);

    // The result DTO's `runId` must land on `id` — it is the only real field the
    // caller could use, and it was `undefined` before this mapping.
    expect(result!.id).toBe('r-1');
    expect(result!.status).toBe('AwaitingApproval');
  });

  it('submit() never fabricates run totals from the action result', () => {
    let result: IPayrollRun | undefined;
    service.submit('r-1').subscribe((r) => (result = r));

    httpMock.expectOne(`${runsUrl}/r-1/submit-for-approval`).flush({
      runId: 'r-1',
      status: 'AwaitingApproval',
    } satisfies ApprovalResultWire);

    // The result DTO carries no money — these are explicit placeholders, and the
    // caller refetches the run. What matters is that nothing invents a total.
    expect(result!.totalGross).toBe(0);
    expect(result!.totalNet).toBe(0);
    expect(result!.totalEmployees).toBe(0);
    expect(result!.initiatedByName).toBeNull();
  });

  // ─── approve (AC-2) ───────────────────────────────────────

  it('approve() POSTs to runs/:id/approve', () => {
    let result: IPayrollRun | undefined;
    service.approve('r-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approve`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...wireActionResult, status: 'Approved', action: 'Approved' });

    expect(result?.status).toBe('Approved');
    expect(result?.id).toBe('r-1');
  });

  it('approve() does NOT report Approved when the server omits the status', () => {
    let result: IPayrollRun | undefined;
    service.approve('r-1').subscribe((r) => (result = r));

    // A response with no status must not read as "this payroll run is approved".
    httpMock.expectOne(`${runsUrl}/r-1/approve`).flush({ runId: 'r-1' });

    expect(result!.status as string).toBe('Unknown');
    expect(result!.status).not.toBe('Approved');
  });

  // ─── reject (AC-3) ────────────────────────────────────────

  it('reject() POSTs the reason to runs/:id/reject', () => {
    let result: IPayrollRun | undefined;
    service
      .reject('r-1', { comments: 'Wrong tax' })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/reject`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ comments: 'Wrong tax' });
    req.flush({ ...wireActionResult, status: 'Rejected', action: 'Rejected' });

    expect(result?.status).toBe('Rejected');
  });

  // ─── return (FR-9) ────────────────────────────────────────

  it('return() POSTs comments to runs/:id/return', () => {
    let result: IPayrollRun | undefined;
    service
      .return('r-1', { comments: 'Please re-check OT' })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/return`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ comments: 'Please re-check OT' });
    req.flush({
      ...wireActionResult,
      status: 'ReviewPending',
      action: 'Returned',
    });

    expect(result?.status).toBe('ReviewPending');
  });

  // ─── finalize (AC-5) ──────────────────────────────────────

  it('finalize() POSTs to runs/:id/finalize', () => {
    let result: IPayrollRun | undefined;
    service.finalize('r-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/finalize`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...wireActionResult, status: 'Finalized', action: 'Approved' });

    expect(result?.status).toBe('Finalized');
  });

  it('finalize() does NOT report Finalized when the server omits the status', () => {
    let result: IPayrollRun | undefined;
    service.finalize('r-1').subscribe((r) => (result = r));

    // The most dangerous possible coercion on this surface: an absent status must
    // never present a run as finalized (payslips locked, money released).
    httpMock.expectOne(`${runsUrl}/r-1/finalize`).flush({ runId: 'r-1' });

    expect(result!.status).not.toBe('Finalized');
    expect(result!.status as string).toBe('Unknown');
  });

  // ─── getApprovalSummary (FR-4) ────────────────────────────

  it('getApprovalSummary() GETs runs/:id/approval-summary', () => {
    let result: IApprovalSummary | undefined;
    service.getApprovalSummary('r-1').subscribe((s) => (result = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approval-summary`);
    expect(req.request.method).toBe('GET');
    req.flush(wireSummary);

    // REGRESSION (live defect): the wire sends `exceptions: string[]`, but the view-model
    // declares objects. Unmapped, every `ex.message` in the approver's exceptions panel
    // rendered blank — the items an approver must read before releasing payroll.
    expect(result).toEqual(mappedSummary);
    expect(result!.exceptions[0].message).toBe(
      '3 payslip(s) have a negative net salary.',
    );
    expect(result!.exceptions[0].severity).toBe('Warning');
    expect(result).not.toBe(wireSummary as unknown as IApprovalSummary);
  });

  it('getApprovalSummary() keeps an absent previous-month net as null, not 0', () => {
    let result: IApprovalSummary | undefined;
    service.getApprovalSummary('r-1').subscribe((s) => (result = s));

    httpMock
      .expectOne(`${runsUrl}/r-1/approval-summary`)
      .flush({ runId: 'r-1', totalNet: 800000 });

    // "No prior run to compare against" is not "the prior run paid nothing" — the
    // variance card renders them differently.
    expect(result!.previousMonthTotalNet).toBeNull();
    expect(result!.variancePercentage).toBeNull();
    // Server-computed totals absent from the payload default to 0.
    expect(result!.totalGross).toBe(0);
    expect(result!.exceptions).toEqual([]);
  });

  // ─── getApprovalHistory (FR-7) ────────────────────────────

  it('getApprovalHistory() GETs runs/:id/approval-history (bare array)', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approval-history`);
    expect(req.request.method).toBe('GET');
    req.flush(wireHistory);

    expect(result).toEqual(mappedHistory);
    // actorName has no wire source — it must stay null, never the actorUserId GUID.
    expect(result![0].actorName).toBeNull();
    expect(result![0]).not.toBe(
      wireHistory[0] as unknown as IApprovalHistoryEntry,
    );
  });

  it('getApprovalHistory() does not relabel an audit row whose action is absent', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    httpMock
      .expectOne(`${runsUrl}/r-1/approval-history`)
      .flush([{ id: 'h-9', stepNumber: 2 }]);

    // This is the approval audit trail — an absent action must NOT be stamped
    // 'Submitted' or 'Approved'. It stays blank so the row cannot lie.
    expect(result![0].action as string).toBe('');
    expect(APPROVAL_ACTION_LABELS[result![0].action]).toBeUndefined();
    expect(result![0].stepNumber).toBe(2);
  });

  it('getApprovalHistory() unwraps a { data } envelope', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    httpMock
      .expectOne(`${runsUrl}/r-1/approval-history`)
      .flush({ data: wireHistory });

    expect(result).toEqual(mappedHistory);
  });

  it('getApprovalHistory() defaults to [] for an unexpected shape', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    httpMock.expectOne(`${runsUrl}/r-1/approval-history`).flush(null);

    expect(result).toEqual([]);
  });

  // ─── listPendingApprovals (§8 queue, DF-14) ───────────────

  const pendingUrl = `${environment.apiBaseUrl}/payroll/approval/pending`;

  // The PendingApprovalDto returned by GET /payroll/approval/pending — note the
  // primary key is `runId` (not `id`) and it carries the approval-step position.
  const mockPendingDto: PendingApprovalWire = {
    runId: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'AwaitingApproval',
    processedEmployees: 247,
    totalEmployees: 250,
    totalGross: 1000000,
    totalNet: 800000,
    submittedBy: '11111111-1111-1111-1111-111111111111',
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    currentApprovalStep: 1,
    totalApprovalSteps: 2,
  };

  it('listPendingApprovals() GETs /payroll/approval/pending and maps runId->id', () => {
    let result: IPayrollRun[] | undefined;
    service.listPendingApprovals().subscribe((r) => (result = r));

    const req = httpMock.expectOne(pendingUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    // No status query param on the dedicated endpoint.
    expect(req.request.params.get('status')).toBeNull();
    req.flush([mockPendingDto]);

    expect(result?.length).toBe(1);
    const mapped = result![0];
    // runId -> id, and initiatedByName surfaces for the "Submitted by" column.
    expect(mapped.id).toBe('r-1');
    expect(mapped.initiatedByName).toBe('Alex HR');
    // Fields the card binds carry through unchanged.
    expect(mapped.payMonth).toBe(5);
    expect(mapped.payYear).toBe(2026);
    expect(mapped.status).toBe('AwaitingApproval');
    expect(mapped.processedEmployees).toBe(247);
    expect(mapped.totalGross).toBe(1000000);
    expect(mapped.totalNet).toBe(800000);
    expect(mapped.initiatedAt).toBe('2026-05-31T10:00:00Z');
    // Derived fields (not rendered) stay consistent.
    expect(mapped.totalDeductions).toBe(200000);
    expect(mapped.skippedEmployees).toBe(3);
    expect(mapped.completedAt).toBeNull();
  });

  it('listPendingApprovals() defaults an absent pending row to the least-claiming values', () => {
    let result: IPayrollRun[] | undefined;
    service.listPendingApprovals().subscribe((r) => (result = r));

    httpMock.expectOne(pendingUrl).flush([{ runId: 'r-7' }]);

    const mapped = result![0];
    expect(mapped.id).toBe('r-7');
    // A queue row with no status must not present as Approved/Finalized.
    expect(mapped.status as string).toBe('Unknown');
    expect(mapped.initiatedByName).toBeNull();
    expect(mapped.totalGross).toBe(0);
    expect(mapped.totalNet).toBe(0);
    expect(mapped.totalDeductions).toBe(0);
    expect(mapped.skippedEmployees).toBe(0);
  });

  it('listPendingApprovals() unwraps a { data } envelope', () => {
    let result: IPayrollRun[] | undefined;
    service.listPendingApprovals().subscribe((r) => (result = r));

    httpMock.expectOne(pendingUrl).flush({ data: [mockPendingDto] });

    expect(result?.length).toBe(1);
    expect(result![0].id).toBe('r-1');
    expect(result![0].initiatedByName).toBe('Alex HR');
  });

  it('listPendingApprovals() defaults to [] for an unexpected shape', () => {
    let result: IPayrollRun[] | undefined;
    service.listPendingApprovals().subscribe((r) => (result = r));

    httpMock.expectOne(pendingUrl).flush(null);

    expect(result).toEqual([]);
  });
});
