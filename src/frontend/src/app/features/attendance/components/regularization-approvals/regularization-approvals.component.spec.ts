import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { RegularizationApprovalsComponent } from './regularization-approvals.component';
import { AttendanceService } from '../../services/attendance.service';
import {
  IPendingRegularization,
  IRegularizationDecisionDto,
  IBulkApproveResult,
} from '../../models/attendance.models';

/**
 * US-ATT-004 component spec (Jasmine + Karma). The AttendanceService is mocked so no
 * HttpClient is exercised. Covers: queue renders pending list (AC-3), approve success
 * removes the row (AC-1), reject requires a >=10-char reason (BR-1), bulk approve of
 * multiple (BR-7), and the authorization/payroll-lock error showing the server
 * message verbatim (AC-5 / BR-5).
 */
describe('RegularizationApprovalsComponent', () => {
  let fixture: ComponentFixture<RegularizationApprovalsComponent>;
  let component: RegularizationApprovalsComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const makeItem = (
    id: string,
    name: string,
    overrides: Partial<IPendingRegularization> = {},
  ): IPendingRegularization => ({
    regularizationId: id,
    employeeId: `emp-${id}`,
    employeeName: name,
    employeePhoto: null,
    date: '2026-06-10',
    regularizationType: 'MISSED_CLOCK_IN',
    requestedClockIn: '2026-06-10T03:30:00Z',
    requestedClockOut: null,
    reason: 'Forgot to clock in this morning while at the client site.',
    submittedOn: '2026-06-11T02:00:00Z',
    ...overrides,
  });

  const items: IPendingRegularization[] = [
    makeItem('reg-1', 'Ada Lovelace'),
    makeItem('reg-2', 'Alan Turing'),
  ];

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

  const approveResult = makeDecision('reg-1', 'APPROVED');

  function setup(initial: IPendingRegularization[] = items): void {
    attendanceSpy.getPendingApprovals.and.returnValue(of(initial));
    fixture = TestBed.createComponent(RegularizationApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getPendingApprovals',
      'processRegularization',
      'bulkApprove',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [RegularizationApprovalsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  // ─── AC-3: queue renders the pending list ───────────────────
  it('renders the pending queue with employee names + count badge', () => {
    setup();
    expect(component.requests().length).toBe(2);
    expect(component.pendingCount()).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
  });

  it('shows the empty state when no requests are pending', () => {
    setup([]);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No pending approvals');
    expect(component.pendingCount()).toBe(0);
  });

  // ─── AC-1: approve success removes the row ──────────────────
  it('approves a request and removes it from the queue (AC-1)', () => {
    setup();
    attendanceSpy.processRegularization.and.returnValue(of(approveResult));

    component.startAction('reg-1', 'approve');
    component.comment.set('Verified against the gate logs.');
    component.confirmAction('reg-1');

    expect(attendanceSpy.processRegularization).toHaveBeenCalledWith(
      'reg-1',
      'APPROVE',
      'Verified against the gate logs.',
    );
    expect(component.requests().length).toBe(1);
    expect(component.requests().some((r) => r.regularizationId === 'reg-1')).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('approves with no comment, sending undefined (BR-2)', () => {
    setup();
    attendanceSpy.processRegularization.and.returnValue(of(approveResult));

    component.startAction('reg-1', 'approve');
    component.comment.set('   ');
    component.confirmAction('reg-1');

    expect(attendanceSpy.processRegularization).toHaveBeenCalledWith(
      'reg-1',
      'APPROVE',
      undefined,
    );
  });

  // ─── BR-1: reject requires a >= 10-char reason ──────────────
  it('blocks reject when the reason is below 10 chars (BR-1)', () => {
    setup();
    component.startAction('reg-1', 'reject');
    component.rejectReason.set('too short');
    expect(component.reasonBelowMin()).toBeTrue();

    component.confirmAction('reg-1');
    expect(attendanceSpy.processRegularization).not.toHaveBeenCalled();
  });

  it('rejects with a valid reason and removes the row (AC-2)', () => {
    setup();
    attendanceSpy.processRegularization.and.returnValue(
      of(makeDecision('reg-1', 'REJECTED')),
    );

    component.startAction('reg-1', 'reject');
    component.rejectReason.set('Requested times do not match the security gate logs.');
    expect(component.reasonBelowMin()).toBeFalse();
    component.confirmAction('reg-1');

    expect(attendanceSpy.processRegularization).toHaveBeenCalledWith(
      'reg-1',
      'REJECT',
      'Requested times do not match the security gate logs.',
    );
    expect(component.requests().length).toBe(1);
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  // ─── BR-7: bulk approve multiple ────────────────────────────
  it('bulk approves selected requests and removes succeeded rows (BR-7)', () => {
    setup();
    const bulkResult: IBulkApproveResult = {
      totalRequested: 2,
      succeededCount: 2,
      failedCount: 0,
      items: [
        { regularizationId: 'reg-1', succeeded: true, decision: makeDecision('reg-1', 'APPROVED') },
        { regularizationId: 'reg-2', succeeded: true, decision: makeDecision('reg-2', 'APPROVED') },
      ],
    };
    attendanceSpy.bulkApprove.and.returnValue(of(bulkResult));

    component.toggleSelect('reg-1');
    component.toggleSelect('reg-2');
    expect(component.selectedIds().size).toBe(2);

    component.bulkApprove();

    expect(attendanceSpy.bulkApprove).toHaveBeenCalledWith(['reg-1', 'reg-2']);
    expect(component.requests().length).toBe(0);
    expect(toastrSpy.success).toHaveBeenCalledWith('2 request(s) approved.');
  });

  it('surfaces per-item failures on a partial bulk approve (BR-5)', () => {
    setup();
    const bulkResult: IBulkApproveResult = {
      totalRequested: 2,
      succeededCount: 1,
      failedCount: 1,
      items: [
        { regularizationId: 'reg-1', succeeded: true, decision: makeDecision('reg-1', 'APPROVED') },
        {
          regularizationId: 'reg-2',
          succeeded: false,
          error: 'This date falls within a locked payroll period. Contact HR.',
          errorCode: 'payroll_period_locked',
        },
      ],
    };
    attendanceSpy.bulkApprove.and.returnValue(of(bulkResult));

    component.toggleSelect('reg-1');
    component.toggleSelect('reg-2');
    component.bulkApprove();

    // Only the succeeded row leaves the queue; the failed one stays.
    expect(component.requests().length).toBe(1);
    expect(component.requests()[0].regularizationId).toBe('reg-2');
    expect(toastrSpy.error).toHaveBeenCalledWith(
      'This date falls within a locked payroll period. Contact HR.',
    );
  });

  it('select-all selects every row, then clears', () => {
    setup();
    component.toggleSelectAll({ target: { checked: true } } as unknown as Event);
    expect(component.selectedIds().size).toBe(2);
    expect(component.allSelected()).toBeTrue();

    component.toggleSelectAll({ target: { checked: false } } as unknown as Event);
    expect(component.selectedIds().size).toBe(0);
  });

  // ─── AC-5 / BR-5: server message shown verbatim ─────────────
  it('shows the AC-5 authorization-denial message verbatim inline', () => {
    setup();
    const msg = 'You are not authorized to approve requests for this employee.';
    attendanceSpy.processRegularization.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 403,
            error: { message: msg, code: 'not_authorized' },
          }),
      ),
    );

    component.startAction('reg-1', 'approve');
    component.confirmAction('reg-1');

    expect(component.actionError()).toBe(msg);
    // Row stays in the queue; the panel stays open for context.
    expect(component.requests().length).toBe(2);
    expect(component.isActioning()).toBeFalse();
  });

  it('shows the BR-5 payroll-lock message verbatim inline', () => {
    setup();
    const msg = 'This date falls within a locked payroll period. Please contact HR.';
    attendanceSpy.processRegularization.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: msg, code: 'payroll_locked' },
          }),
      ),
    );

    // 409 path re-syncs the queue; stub the reload.
    attendanceSpy.getPendingApprovals.and.returnValue(of(items));

    component.startAction('reg-1', 'reject');
    component.rejectReason.set('Cannot regularize this locked period.');
    component.confirmAction('reg-1');

    // 409 surfaces via toast + refresh (already handled elsewhere).
    expect(toastrSpy.error).toHaveBeenCalledWith(msg);
  });

  it('surfaces a queue-load failure via toast', () => {
    attendanceSpy.getPendingApprovals.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    fixture = TestBed.createComponent(RegularizationApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
  });
});
