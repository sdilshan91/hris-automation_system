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
  IApprovalHistoryEntry,
  IApprovalSummary,
} from '../models/approval.models';

describe('PayrollApprovalService (US-PAY-008)', () => {
  let service: PayrollApprovalService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  const mockRun: IPayrollRun = {
    id: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'AwaitingApproval',
    totalEmployees: 250,
    processedEmployees: 247,
    skippedEmployees: 3,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalNet: 800000,
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
  };

  const mockSummary: IApprovalSummary = {
    runId: 'r-1',
    totalEmployees: 250,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalStatutory: 120000,
    totalNet: 800000,
    previousMonthTotalNet: 760000,
    variancePercentage: 5.26,
    exceptions: [
      { severity: 'Warning', message: 'Negative net', employeeName: 'Sam' },
    ],
  };

  const mockHistory: IApprovalHistoryEntry[] = [
    {
      id: 'h-1',
      stepNumber: 1,
      action: 'Submitted',
      actorName: 'Alex HR',
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
    req.flush(mockRun);

    expect(result).toEqual(mockRun);
  });

  // ─── approve (AC-2) ───────────────────────────────────────

  it('approve() POSTs to runs/:id/approve', () => {
    let result: IPayrollRun | undefined;
    service.approve('r-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approve`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockRun, status: 'Approved' });

    expect(result?.status).toBe('Approved');
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
    req.flush({ ...mockRun, status: 'Rejected' });

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
    req.flush({ ...mockRun, status: 'ReviewPending' });

    expect(result?.status).toBe('ReviewPending');
  });

  // ─── finalize (AC-5) ──────────────────────────────────────

  it('finalize() POSTs to runs/:id/finalize', () => {
    let result: IPayrollRun | undefined;
    service.finalize('r-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${runsUrl}/r-1/finalize`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockRun, status: 'Finalized' });

    expect(result?.status).toBe('Finalized');
  });

  // ─── getApprovalSummary (FR-4) ────────────────────────────

  it('getApprovalSummary() GETs runs/:id/approval-summary', () => {
    let result: IApprovalSummary | undefined;
    service.getApprovalSummary('r-1').subscribe((s) => (result = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approval-summary`);
    expect(req.request.method).toBe('GET');
    req.flush(mockSummary);

    expect(result).toEqual(mockSummary);
  });

  // ─── getApprovalHistory (FR-7) ────────────────────────────

  it('getApprovalHistory() GETs runs/:id/approval-history (bare array)', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    const req = httpMock.expectOne(`${runsUrl}/r-1/approval-history`);
    expect(req.request.method).toBe('GET');
    req.flush(mockHistory);

    expect(result).toEqual(mockHistory);
  });

  it('getApprovalHistory() unwraps a { data } envelope', () => {
    let result: IApprovalHistoryEntry[] | undefined;
    service.getApprovalHistory('r-1').subscribe((h) => (result = h));

    httpMock
      .expectOne(`${runsUrl}/r-1/approval-history`)
      .flush({ data: mockHistory });

    expect(result).toEqual(mockHistory);
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
  const mockPendingDto = {
    runId: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'AwaitingApproval' as const,
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
