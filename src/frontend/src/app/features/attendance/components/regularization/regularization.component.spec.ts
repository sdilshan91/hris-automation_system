import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { RegularizationComponent } from './regularization.component';
import { AttendanceService } from '../../services/attendance.service';
import { IRegularization } from '../../models/attendance.models';

/**
 * US-ATT-003 spec: form validation (reason min-length, future date, time order),
 * successful submit + pending pill, and rejection handling (server message verbatim).
 * The AttendanceService is mocked so no HttpClient is exercised here.
 */
describe('RegularizationComponent', () => {
  let fixture: ComponentFixture<RegularizationComponent>;
  let component: RegularizationComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const existing: IRegularization = {
    regularizationId: 'reg-0',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    attendanceLogId: null,
    date: '2026-06-10',
    regularizationType: 'MISSED_CLOCK_IN',
    requestedClockIn: '2026-06-10T03:30:00Z',
    requestedClockOut: null,
    reason: 'Forgot to clock in this morning.',
    status: 'PENDING',
    createdAt: '2026-06-11T02:00:00Z',
  };

  const created: IRegularization = {
    regularizationId: 'reg-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    attendanceLogId: null,
    date: '2026-06-12',
    regularizationType: 'MISSED_BOTH',
    requestedClockIn: '2026-06-12T03:30:00Z',
    requestedClockOut: '2026-06-12T11:30:00Z',
    reason: 'Offsite meeting all day, forgot both punches.',
    status: 'PENDING',
    createdAt: '2026-06-13T02:00:00Z',
  };

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'listRegularizations',
      'submitRegularization',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    attendanceSpy.listRegularizations.and.returnValue(of([existing]));

    await TestBed.configureTestingModule({
      imports: [RegularizationComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegularizationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load existing requests', () => {
    expect(component).toBeTruthy();
    expect(component.requests().length).toBe(1);
    expect(attendanceSpy.listRegularizations).toHaveBeenCalled();
  });

  it('should render an existing PENDING status pill', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Pending');
  });

  // ─── Validation ──────────────────────────────────────────

  it('should flag reason below the 10-character minimum (BR-7)', () => {
    component.openForm();
    component.form.patchValue({
      regularizationType: 'MISSED_CLOCK_IN',
      requestedClockIn: '09:00',
      reason: 'too short',
    });
    expect(component.reasonLength()).toBe(9);
    expect(component.reasonBelowMin()).toBeTrue();
    expect(component.form.get('reason')?.invalid).toBeTrue();
  });

  it('should accept a reason at or above the minimum length', () => {
    component.openForm();
    component.form.patchValue({ reason: 'A perfectly valid ten-plus char reason.' });
    expect(component.reasonBelowMin()).toBeFalse();
    expect(component.form.get('reason')?.valid).toBeTrue();
  });

  it('should reject a future date (BR-4)', () => {
    component.openForm();
    component.form.patchValue({ date: '2999-12-31' });
    expect(component.form.errors?.['futureDate']).toBeTrue();
  });

  it('should require clock-in time for MISSED_CLOCK_IN and surface requiresClockIn', () => {
    component.openForm();
    component.form.patchValue({ regularizationType: 'MISSED_CLOCK_IN', requestedClockIn: '' });
    expect(component.requiresClockIn()).toBeTrue();
    expect(component.requiresClockOut()).toBeFalse();
    expect(component.form.errors?.['clockInRequired']).toBeTrue();
  });

  it('should require clock-out time for MISSED_CLOCK_OUT', () => {
    component.openForm();
    component.form.patchValue({ regularizationType: 'MISSED_CLOCK_OUT', requestedClockOut: '' });
    expect(component.requiresClockOut()).toBeTrue();
    expect(component.form.errors?.['clockOutRequired']).toBeTrue();
  });

  it('should enforce clock-in before clock-out (FR-5)', () => {
    component.openForm();
    component.form.patchValue({
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '17:00',
      requestedClockOut: '09:00',
    });
    expect(component.form.errors?.['timeOrder']).toBeTrue();
  });

  it('should accept correctly ordered times', () => {
    component.openForm();
    component.form.patchValue({
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '09:00',
      requestedClockOut: '17:30',
    });
    expect(component.form.errors?.['timeOrder']).toBeUndefined();
  });

  it('should not submit an invalid form and should warn', () => {
    component.openForm();
    component.form.patchValue({ regularizationType: '', reason: '' });
    component.submit();
    expect(attendanceSpy.submitRegularization).not.toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalled();
  });

  // ─── Successful submit + pending pill ────────────────────

  it('should submit a valid request, prepend the PENDING record, and close the drawer', fakeAsync(() => {
    attendanceSpy.submitRegularization.and.returnValue(of(created));

    component.openForm();
    component.form.patchValue({
      date: '2026-06-12',
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '09:00',
      requestedClockOut: '17:30',
      reason: 'Offsite meeting all day, forgot both punches.',
    });

    component.submit();
    tick();
    fixture.detectChanges();

    expect(attendanceSpy.submitRegularization).toHaveBeenCalledOnceWith({
      date: '2026-06-12',
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '09:00',
      requestedClockOut: '17:30',
      reason: 'Offsite meeting all day, forgot both punches.',
    });
    expect(component.requests()[0].regularizationId).toBe('reg-1');
    expect(component.requests().length).toBe(2);
    expect(component.formOpen()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();

    // Pending pill renders in the list for the new record.
    expect(fixture.nativeElement.textContent).toContain('Pending');
  }));

  it('should null out times not required by the selected type', fakeAsync(() => {
    attendanceSpy.submitRegularization.and.returnValue(of(created));

    component.openForm();
    component.form.patchValue({
      date: '2026-06-12',
      regularizationType: 'MISSED_CLOCK_IN',
      requestedClockIn: '09:00',
      requestedClockOut: '17:30', // should be dropped — type only needs clock-in
      reason: 'Forgot to clock in before the standup.',
    });

    component.submit();
    tick();

    const arg = attendanceSpy.submitRegularization.calls.mostRecent().args[0];
    expect(arg.requestedClockIn).toBe('09:00');
    expect(arg.requestedClockOut).toBeNull();
  }));

  // ─── Rejection handling ──────────────────────────────────

  it('should display the server rejection message verbatim and keep the drawer open (AC-3/4/5)', fakeAsync(() => {
    const serverMessage = 'Regularization requests can only be submitted for the last 7 days.';
    const err = new HttpErrorResponse({
      status: 400,
      error: { message: serverMessage, code: 'lookback_exceeded' },
    });
    attendanceSpy.submitRegularization.and.returnValue(throwError(() => err));

    component.openForm();
    component.form.patchValue({
      date: '2026-06-01',
      regularizationType: 'MISSED_CLOCK_IN',
      requestedClockIn: '09:00',
      reason: 'Forgot to clock in on the first of the month.',
    });

    component.submit();
    tick();
    fixture.detectChanges();

    expect(component.serverError()).toBe(serverMessage);
    expect(component.formOpen()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain(serverMessage);
  }));

  it('should fall back to a generic message when the error body has no message', fakeAsync(() => {
    const err = new HttpErrorResponse({ status: 500, error: 'boom' });
    attendanceSpy.submitRegularization.and.returnValue(throwError(() => err));

    component.openForm();
    component.form.patchValue({
      date: '2026-06-12',
      regularizationType: 'MISSED_CLOCK_OUT',
      requestedClockOut: '17:30',
      reason: 'Forgot to clock out after the late shift.',
    });

    component.submit();
    tick();

    expect(component.serverError()).toContain('unexpected error');
    expect(component.formOpen()).toBeTrue();
  }));

  // ─── Drawer open/close ───────────────────────────────────

  it('openForm should pre-populate the date with today and clear server error', () => {
    component.serverError.set('stale');
    component.openForm();
    expect(component.formOpen()).toBeTrue();
    expect(component.form.get('date')?.value).toBeTruthy();
    expect(component.serverError()).toBeNull();
  });

  it('closeForm should not close while submitting', () => {
    component.openForm();
    component.isSubmitting.set(true);
    component.closeForm();
    expect(component.formOpen()).toBeTrue();
  });
});
