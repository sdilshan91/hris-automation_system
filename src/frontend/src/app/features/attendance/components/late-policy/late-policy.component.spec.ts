import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { LatePolicyComponent } from './late-policy.component';
import { AttendanceService } from '../../services/attendance.service';
import { ILatePolicy } from '../../models/attendance.models';

/**
 * US-ATT-008 (AC-4) HR late-policy config spec. AttendanceService is mocked — no
 * HttpClient. Covers: the form loads the policy (GET), saves edits (PUT), blocks an
 * invalid save, and surfaces a load failure on defaults.
 */
describe('LatePolicyComponent', () => {
  let fixture: ComponentFixture<LatePolicyComponent>;
  let component: LatePolicyComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const policy: ILatePolicy = {
    thresholdCount: 4,
    deductionDays: 1,
    period: 'QUARTERLY',
    notificationOnLate: false,
    chronicThreshold: 7,
    isActive: true,
  };

  function setup(getRes = of(policy)): void {
    attendanceSpy.getLatePolicy.and.returnValue(getRes);
    fixture = TestBed.createComponent(LatePolicyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getLatePolicy',
      'updateLatePolicy',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [LatePolicyComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('loads the policy into the form (AC-4)', () => {
    setup();
    expect(component.isLoading()).toBeFalse();
    expect(component.form.getRawValue()).toEqual(policy);
  });

  it('saves the edited policy via PUT (AC-4)', () => {
    setup();
    attendanceSpy.updateLatePolicy.and.returnValue(of(policy));
    component.form.patchValue({ thresholdCount: 5 });
    component.onSubmit();
    expect(attendanceSpy.updateLatePolicy).toHaveBeenCalledWith(
      jasmine.objectContaining({ thresholdCount: 5 }),
    );
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  it('blocks save when the form is invalid', () => {
    setup();
    component.form.patchValue({ thresholdCount: 0 }); // min(1) violated
    component.onSubmit();
    expect(attendanceSpy.updateLatePolicy).not.toHaveBeenCalled();
    expect(component.form.touched).toBeTrue();
  });

  it('surfaces a save failure as a toast', () => {
    setup();
    attendanceSpy.updateLatePolicy.and.returnValue(
      throwError(() => new Error('boom')),
    );
    component.onSubmit();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  it('keeps defaults and toasts on a load failure', () => {
    setup(throwError(() => new Error('nope')));
    expect(component.isLoading()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
    // defaults remain usable
    expect(component.form.get('thresholdCount')?.value).toBe(3);
  });
});
