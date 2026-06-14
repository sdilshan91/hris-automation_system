import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { OvertimeApprovalsComponent } from './overtime-approvals.component';
import { AttendanceService } from '../../services/attendance.service';
import { IOvertimeQueueItem, IOvertimeDecision } from '../../models/attendance.models';

/**
 * US-ATT-006 manager overtime-approvals spec. AttendanceService is mocked — no
 * HttpClient. Covers: queue renders (AC-3), approve with an adjusted-minutes value
 * (FR-6) removes the row (AC-4), reject requires a >=10-char reason, and the
 * self-approval/lock error showing the server message verbatim (BR-8).
 */
describe('OvertimeApprovalsComponent', () => {
  let fixture: ComponentFixture<OvertimeApprovalsComponent>;
  let component: OvertimeApprovalsComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const makeItem = (
    id: string,
    name: string,
    overrides: Partial<IOvertimeQueueItem> = {},
  ): IOvertimeQueueItem => ({
    id,
    employeeId: `emp-${id}`,
    employeeName: name,
    employeePhoto: null,
    attendanceLogId: `log-${id}`,
    date: '2026-06-10',
    overtimeMinutes: 180,
    approvedMinutes: null,
    multiplier: 1.5,
    type: 'AUTO_DETECTED',
    status: 'PENDING',
    reason: 'Stayed late to ship the release candidate.',
    managerComment: null,
    createdAt: '2026-06-10T18:00:00Z',
    submittedOn: '2026-06-10T18:05:00Z',
    ...overrides,
  });

  const items: IOvertimeQueueItem[] = [
    makeItem('ot-1', 'Ada Lovelace'),
    makeItem('ot-2', 'Alan Turing'),
  ];

  const decision = (id: string, status: 'APPROVED' | 'REJECTED', approved: number | null): IOvertimeDecision => ({
    id,
    status,
    approvedMinutes: approved,
    multiplier: 1.5,
    actionedAt: '2026-06-11T03:00:00Z',
  });

  function setup(initial: IOvertimeQueueItem[] = items): void {
    attendanceSpy.getPendingOvertime.and.returnValue(of(initial));
    fixture = TestBed.createComponent(OvertimeApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getPendingOvertime',
      'approveOvertime',
      'rejectOvertime',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [OvertimeApprovalsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('renders the pending overtime queue with names + count (AC-3)', () => {
    setup();
    expect(component.requests().length).toBe(2);
    expect(component.pendingCount()).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
  });

  it('shows the empty state when nothing is pending', () => {
    setup([]);
    expect((fixture.nativeElement.textContent as string)).toContain('No pending overtime');
  });

  // ─── AC-4 / FR-6: approve with adjusted minutes ─────────────
  it('approves with an adjusted minutes value and removes the row (FR-6)', () => {
    setup();
    attendanceSpy.approveOvertime.and.returnValue(of(decision('ot-1', 'APPROVED', 120)));

    component.startAction('ot-1', 'approve');
    component.adjustedMinutes.set(120);
    component.comment.set('Approved 2h of the 3h requested.');
    component.confirmAction(items[0]);

    expect(attendanceSpy.approveOvertime).toHaveBeenCalledWith(
      'ot-1',
      120,
      'Approved 2h of the 3h requested.',
    );
    expect(component.requests().length).toBe(1);
    expect(component.requests().some((r) => r.id === 'ot-1')).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('approves the full amount when no adjustment is entered', () => {
    setup();
    attendanceSpy.approveOvertime.and.returnValue(of(decision('ot-1', 'APPROVED', 180)));

    component.startAction('ot-1', 'approve');
    component.confirmAction(items[0]);

    // approvedMinutes omitted (undefined), comment omitted (undefined).
    expect(attendanceSpy.approveOvertime).toHaveBeenCalledWith('ot-1', undefined, undefined);
  });

  // ─── Reject min-10 ──────────────────────────────────────────
  it('blocks reject until the reason reaches 10 chars', () => {
    setup();
    component.startAction('ot-1', 'reject');
    component.rejectReason.set('too short');
    expect(component.reasonBelowMin()).toBeTrue();

    component.confirmAction(items[0]);
    expect(attendanceSpy.rejectOvertime).not.toHaveBeenCalled();
  });

  it('rejects with a valid reason and removes the row', () => {
    setup();
    attendanceSpy.rejectOvertime.and.returnValue(of(decision('ot-1', 'REJECTED', null)));

    component.startAction('ot-1', 'reject');
    component.rejectReason.set('Overtime was not pre-approved for this period.');
    expect(component.reasonBelowMin()).toBeFalse();
    component.confirmAction(items[0]);

    expect(attendanceSpy.rejectOvertime).toHaveBeenCalledWith(
      'ot-1',
      'Overtime was not pre-approved for this period.',
    );
    expect(component.requests().some((r) => r.id === 'ot-1')).toBeFalse();
  });

  // ─── BR-8: self-approval error shown verbatim ───────────────
  it('shows the self-approval error message verbatim inline (BR-8)', () => {
    setup();
    const err = new HttpErrorResponse({
      status: 403,
      error: { message: 'You cannot approve your own overtime.', code: 'self_approval' },
    });
    attendanceSpy.approveOvertime.and.returnValue(throwError(() => err));

    component.startAction('ot-1', 'approve');
    component.confirmAction(items[0]);

    expect(component.actionError()).toBe('You cannot approve your own overtime.');
    // Row stays in the queue so the manager can re-route it.
    expect(component.requests().some((r) => r.id === 'ot-1')).toBeTrue();
    expect(component.isActioning()).toBeFalse();
  });

  it('toasts + reloads on a 409 already-actioned conflict', () => {
    setup();
    const err = new HttpErrorResponse({
      status: 409,
      error: { message: 'This overtime was already actioned.', code: 'already_actioned' },
    });
    attendanceSpy.approveOvertime.and.returnValue(throwError(() => err));
    attendanceSpy.getPendingOvertime.and.returnValue(of(items));

    component.startAction('ot-1', 'approve');
    component.confirmAction(items[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith('This overtime was already actioned.');
    expect(attendanceSpy.getPendingOvertime).toHaveBeenCalledTimes(2);
  });
});
