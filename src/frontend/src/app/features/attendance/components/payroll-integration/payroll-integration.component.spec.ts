import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { PayrollIntegrationComponent } from './payroll-integration.component';
import { AttendanceService } from '../../services/attendance.service';
import {
  IPeriodLock,
  IReconciliationResult,
  IAttendancePayrollResult,
} from '../../models/attendance.models';

/**
 * US-ATT-009 payroll-integration spec. AttendanceService is mocked — no HttpClient.
 * Covers: lock-status load + prominent banner, the lock action (confirm modal -> POST),
 * the unlock action, the reconciliation table render (incl. pending-payroll placeholder),
 * and the on-demand payroll-data feed fetch.
 */
describe('PayrollIntegrationComponent', () => {
  let fixture: ComponentFixture<PayrollIntegrationComponent>;
  let component: PayrollIntegrationComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const lockedLock: IPeriodLock = {
    id: 'lock-1',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-31',
    isLocked: true,
    lockedBy: 'hr-1',
    lockedAt: '2026-06-01T09:00:00Z',
    unlockedBy: null,
    unlockedAt: null,
  };

  const reconResult: IReconciliationResult = {
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
      {
        employeeId: 'e2',
        employeeName: 'Alan Turing',
        presentDays: 22,
        lopDays: 0,
        approvedOvertimeMinutes: 0,
        totalWorkMinutes: 10560,
      },
    ],
  };

  const payrollResult: IAttendancePayrollResult = {
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

  /** Initialise the component; pass `lock` to seed the lock state (null = never locked). */
  function setup(lock: IPeriodLock | null = null): void {
    attendanceSpy.getPeriodLock.and.returnValue(of(lock));
    attendanceSpy.getReconciliation.and.returnValue(of(reconResult));
    fixture = TestBed.createComponent(PayrollIntegrationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getPeriodLock',
      'getReconciliation',
      'getPayrollData',
      'lockPeriod',
      'unlockPeriod',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [PayrollIntegrationComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('loads the lock status and reconciliation on init', () => {
    setup(null);
    expect(attendanceSpy.getPeriodLock).toHaveBeenCalled();
    expect(attendanceSpy.getReconciliation).toHaveBeenCalled();
    expect(component.isLocked()).toBeFalse();
  });

  it('shows the prominent locked banner when the period is locked', () => {
    setup(lockedLock);
    const banner = fixture.nativeElement.querySelector('[data-test="locked-banner"]');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('Attendance locked');
    // Lock button is replaced by Unlock when locked.
    expect(fixture.nativeElement.querySelector('[data-test="unlock-btn"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-test="lock-btn"]')).toBeNull();
  });

  it('does NOT show the banner when the period is open', () => {
    setup(null);
    expect(fixture.nativeElement.querySelector('[data-test="locked-banner"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-test="lock-btn"]')).toBeTruthy();
  });

  it('opening the lock action shows the confirm modal then POSTs on confirm (AC-4)', () => {
    setup(null);
    attendanceSpy.lockPeriod.and.returnValue(of(lockedLock));

    component.openLockConfirm();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-test="confirm-modal"]')).toBeTruthy();
    expect(component.confirmMode()).toBe('lock');

    component.confirmLock();
    fixture.detectChanges();

    // Posts the full-month range for the selected month.
    expect(attendanceSpy.lockPeriod).toHaveBeenCalled();
    expect(component.isLocked()).toBeTrue();
    expect(component.confirmMode()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
    // Banner now visible after lock.
    expect(fixture.nativeElement.querySelector('[data-test="locked-banner"]')).toBeTruthy();
  });

  it('surfaces a lock failure via toastr and keeps the modal state recoverable', () => {
    setup(null);
    attendanceSpy.lockPeriod.and.returnValue(throwError(() => new Error('boom')));

    component.openLockConfirm();
    component.confirmLock();
    fixture.detectChanges();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isLocked()).toBeFalse();
    expect(component.acting()).toBeFalse();
  });

  it('unlock action POSTs the unlock and clears the locked state (AC-5)', () => {
    setup(lockedLock);
    attendanceSpy.unlockPeriod.and.returnValue(of({ ...lockedLock, isLocked: false }));

    component.openUnlockConfirm();
    fixture.detectChanges();
    expect(component.confirmMode()).toBe('unlock');

    component.confirmUnlock();
    fixture.detectChanges();

    expect(attendanceSpy.unlockPeriod).toHaveBeenCalledWith('lock-1');
    expect(component.isLocked()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-test="locked-banner"]')).toBeNull();
  });

  it('renders the reconciliation table with attendance rows and a pending-payroll placeholder (FR-5)', () => {
    setup(null);
    const rows = fixture.nativeElement.querySelectorAll('[data-test="recon-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Ada Lovelace');
    // LOP cell reflects the value.
    const lopCell = rows[0].querySelector('[data-test="recon-lop"]');
    expect(lopCell.textContent).toContain('2');
    // Payroll columns are placeholders.
    const pending = rows[0].querySelector('[data-test="recon-pending"]');
    expect(pending.textContent).toContain('—');
  });

  it('shows the reconciliation empty state when there are no rows', () => {
    attendanceSpy.getPeriodLock.and.returnValue(of(null));
    attendanceSpy.getReconciliation.and.returnValue(of({ period: '2026-05', rows: [] }));
    fixture = TestBed.createComponent(PayrollIntegrationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-test="recon-empty"]')).toBeTruthy();
  });

  it('lazily fetches the payroll-data feed only when the preview is opened (FR-1/FR-2)', () => {
    setup(null);
    attendanceSpy.getPayrollData.and.returnValue(of(payrollResult));
    expect(attendanceSpy.getPayrollData).not.toHaveBeenCalled();

    component.togglePreview();
    fixture.detectChanges();

    expect(attendanceSpy.getPayrollData).toHaveBeenCalledOnceWith(component.month());
    expect(component.previewRows().length).toBe(1);
    expect(fixture.nativeElement.querySelector('[data-test="preview-table"]')).toBeTruthy();

    // Re-opening does not re-fetch (cached after first load).
    component.togglePreview();
    component.togglePreview();
    expect(attendanceSpy.getPayrollData).toHaveBeenCalledTimes(1);
  });

  it('renders the payroll-process stepper with only Lock Attendance actionable (§8)', () => {
    setup(null);
    const steps = fixture.nativeElement.querySelectorAll('[data-test="step"]');
    expect(steps.length).toBe(5);
    expect(steps[0].textContent).toContain('Lock Attendance');
    expect(steps[1].textContent).toContain('Payroll module pending');
  });

  it('changing the month reloads lock + reconciliation', () => {
    setup(null);
    attendanceSpy.getPeriodLock.calls.reset();
    attendanceSpy.getReconciliation.calls.reset();

    component.onMonthChange('2026-04');
    expect(component.month()).toBe('2026-04');
    expect(attendanceSpy.getPeriodLock).toHaveBeenCalled();
    expect(attendanceSpy.getReconciliation).toHaveBeenCalled();
  });
});
