import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveTypeFormComponent } from './leave-type-form.component';
import { LeaveTypeService } from '../../services/leave-type.service';
import { ILeaveType, getContrastTextColor } from '../../models/leave-type.models';

/** Namespace alias for pure-function tests in the separate top-level describe. */
const leaveTypeModels = { getContrastTextColor };

describe('LeaveTypeFormComponent', () => {
  let component: LeaveTypeFormComponent;
  let fixture: ComponentFixture<LeaveTypeFormComponent>;
  let leaveTypeServiceSpy: jasmine.SpyObj<LeaveTypeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockLeaveType: ILeaveType = {
    leaveTypeId: 'lt-1',
    tenantId: 'tenant-1',
    name: 'Annual Leave',
    code: 'AL',
    color: '#2563eb',
    description: 'Paid annual leave',
    annualEntitlement: 20,
    accrualFrequency: 'monthly',
    carryForwardLimit: 5,
    carryForwardExpiryMonths: 3,
    probationEligible: false,
    documentsRequired: false,
    documentDayThreshold: null,
    encashable: true,
    maxEncashDays: 10,
    halfDayAllowed: true,
    hourlyAllowed: false,
    gender: 'all',
    maxConsecutiveDays: 15,
    negativeBalanceAllowed: false,
    negativeBalanceLimit: null,
    displayOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  beforeEach(async () => {
    leaveTypeServiceSpy = jasmine.createSpyObj('LeaveTypeService', [
      'createLeaveType',
      'updateLeaveType',
    ]);

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [LeaveTypeFormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        { provide: LeaveTypeService, useValue: leaveTypeServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  function createComponent(leaveType: ILeaveType | null = null): void {
    fixture = TestBed.createComponent(LeaveTypeFormComponent);
    component = fixture.componentInstance;
    // Set input using ComponentRef
    fixture.componentRef.setInput('leaveType', leaveType);
    fixture.detectChanges();
  }

  // --- Create Mode ---

  it('should create in create mode', () => {
    createComponent();
    expect(component).toBeTruthy();
    expect(component.form).toBeTruthy();
  });

  it('should have empty form in create mode', () => {
    createComponent();
    expect(component.form.get('name')?.value).toBe('');
    expect(component.form.get('code')?.value).toBe('');
    expect(component.form.get('annualEntitlement')?.value).toBe(0);
    expect(component.form.get('accrualFrequency')?.value).toBe('yearly');
  });

  it('should require name and code', () => {
    createComponent();
    expect(component.form.get('name')?.hasError('required')).toBeTrue();
    expect(component.form.get('code')?.hasError('required')).toBeTrue();
    expect(component.form.valid).toBeFalse();
  });

  it('should validate code format (alphanumeric)', () => {
    createComponent();
    component.form.get('code')?.setValue('AL@#');
    expect(component.form.get('code')?.hasError('pattern')).toBeTrue();

    component.form.get('code')?.setValue('AL-01');
    expect(component.form.get('code')?.hasError('pattern')).toBeFalse();
  });

  it('should validate name max length', () => {
    createComponent();
    component.form.get('name')?.setValue('A'.repeat(101));
    expect(component.form.get('name')?.hasError('maxlength')).toBeTrue();
  });

  it('should validate annual entitlement min value', () => {
    createComponent();
    component.form.get('annualEntitlement')?.setValue(-1);
    expect(component.form.get('annualEntitlement')?.hasError('min')).toBeTrue();

    component.form.get('annualEntitlement')?.setValue(0);
    expect(component.form.get('annualEntitlement')?.hasError('min')).toBeFalse();
  });

  // --- Create submit ---

  it('should submit create form and emit saved', () => {
    createComponent();
    const createdLt = { ...mockLeaveType, leaveTypeId: 'lt-new' };
    leaveTypeServiceSpy.createLeaveType.and.returnValue(of(createdLt));

    spyOn(component.saved, 'emit');

    component.form.patchValue({
      name: 'Test Leave',
      code: 'TL',
      color: '#2563eb',
      annualEntitlement: 10,
      accrualFrequency: 'yearly',
    });
    component.form.markAsDirty();

    component.onSubmit();

    expect(leaveTypeServiceSpy.createLeaveType).toHaveBeenCalled();
    const payload = leaveTypeServiceSpy.createLeaveType.calls.mostRecent().args[0];
    expect(payload.name).toBe('Test Leave');
    expect(payload.code).toBe('TL');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.saved.emit).toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  it('should not submit when form is invalid', () => {
    createComponent();
    component.onSubmit();
    expect(leaveTypeServiceSpy.createLeaveType).not.toHaveBeenCalled();
  });

  // --- Duplicate name error handling (AC-3) ---

  it('should display duplicate name error from backend', () => {
    createComponent();
    leaveTypeServiceSpy.createLeaveType.and.returnValue(
      throwError(() => ({
        status: 409,
        error: {
          message: 'A leave type with this name already exists',
          code: 'duplicate_name',
        },
      }))
    );

    component.form.patchValue({
      name: 'Annual Leave',
      code: 'AL2',
      color: '#2563eb',
      annualEntitlement: 10,
      accrualFrequency: 'yearly',
    });
    component.form.markAsDirty();

    component.onSubmit();

    expect(component.duplicateNameError()).toBe(
      'A leave type with this name already exists'
    );
    expect(toastrSpy.error).not.toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  it('should show toast for generic errors', () => {
    createComponent();
    leaveTypeServiceSpy.createLeaveType.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unexpected error' },
      }))
    );

    component.form.patchValue({
      name: 'Test',
      code: 'TST',
      color: '#2563eb',
      annualEntitlement: 5,
      accrualFrequency: 'yearly',
    });
    component.form.markAsDirty();

    component.onSubmit();

    expect(toastrSpy.error).toHaveBeenCalledWith('Unexpected error');
    expect(component.duplicateNameError()).toBe('');
  });

  // --- Edit Mode ---

  it('should populate form in edit mode', () => {
    createComponent(mockLeaveType);
    expect(component.form.get('name')?.value).toBe('Annual Leave');
    expect(component.form.get('code')?.value).toBe('AL');
    expect(component.form.get('color')?.value).toBe('#2563eb');
    expect(component.form.get('annualEntitlement')?.value).toBe(20);
    expect(component.form.get('accrualFrequency')?.value).toBe('monthly');
  });

  it('should auto-expand advanced section in edit mode when advanced fields differ from defaults', () => {
    createComponent(mockLeaveType);
    // mockLeaveType has encashable=true which differs from default
    expect(component.advancedExpanded()).toBeTrue();
  });

  it('should submit update in edit mode', () => {
    createComponent(mockLeaveType);
    leaveTypeServiceSpy.updateLeaveType.and.returnValue(
      of({ ...mockLeaveType, name: 'Updated Leave' })
    );

    spyOn(component.saved, 'emit');

    component.form.get('name')?.setValue('Updated Leave');
    component.form.markAsDirty();

    component.onSubmit();

    expect(leaveTypeServiceSpy.updateLeaveType).toHaveBeenCalledWith(
      'lt-1',
      jasmine.objectContaining({ name: 'Updated Leave' })
    );
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.saved.emit).toHaveBeenCalled();
  });

  // --- Form sections and grouped fields ---

  it('should toggle control values', () => {
    createComponent();
    expect(component.form.get('probationEligible')?.value).toBeFalse();
    component.toggleControl('probationEligible');
    expect(component.form.get('probationEligible')?.value).toBeTrue();
  });

  it('should select a color', () => {
    createComponent();
    component.selectColor('#dc2626');
    expect(component.form.get('color')?.value).toBe('#dc2626');
  });

  it('should clear document threshold when documents not required', () => {
    createComponent();
    component.form.patchValue({
      name: 'Test',
      code: 'TST',
      color: '#2563eb',
      annualEntitlement: 5,
      accrualFrequency: 'yearly',
      documentsRequired: true,
      documentDayThreshold: 3,
    });
    component.form.markAsDirty();

    // Toggle documents off
    component.form.get('documentsRequired')?.setValue(false);
    leaveTypeServiceSpy.createLeaveType.and.returnValue(
      of(mockLeaveType)
    );

    component.onSubmit();

    const payload = leaveTypeServiceSpy.createLeaveType.calls.mostRecent().args[0];
    expect(payload.documentDayThreshold).toBeNull();
  });

  it('should clear encash days when not encashable', () => {
    createComponent();
    component.form.patchValue({
      name: 'Test',
      code: 'TST',
      color: '#2563eb',
      annualEntitlement: 5,
      accrualFrequency: 'yearly',
      encashable: false,
      maxEncashDays: 10,
    });
    component.form.markAsDirty();
    leaveTypeServiceSpy.createLeaveType.and.returnValue(
      of(mockLeaveType)
    );

    component.onSubmit();

    const payload = leaveTypeServiceSpy.createLeaveType.calls.mostRecent().args[0];
    expect(payload.maxEncashDays).toBeNull();
  });

  it('should clear negative balance limit when negative balance not allowed', () => {
    createComponent();
    component.form.patchValue({
      name: 'Test',
      code: 'TST',
      color: '#2563eb',
      annualEntitlement: 5,
      accrualFrequency: 'yearly',
      negativeBalanceAllowed: false,
      negativeBalanceLimit: 5,
    });
    component.form.markAsDirty();
    leaveTypeServiceSpy.createLeaveType.and.returnValue(
      of(mockLeaveType)
    );

    component.onSubmit();

    const payload = leaveTypeServiceSpy.createLeaveType.calls.mostRecent().args[0];
    expect(payload.negativeBalanceLimit).toBeNull();
  });

  // --- Cancelled output ---

  it('should emit cancelled when cancel is clicked', () => {
    createComponent();
    spyOn(component.cancelled, 'emit');
    const cancelBtn = fixture.nativeElement.querySelector('.btn-secondary') as HTMLButtonElement;
    cancelBtn.click();
    expect(component.cancelled.emit).toHaveBeenCalled();
  });
});

/**
 * Pure function tests -- separate top-level describe to avoid
 * httpMock.verify() conflicts.
 */
describe('LeaveTypeFormComponent helpers (pure functions)', () => {
  it('getContrastTextColor should return white for dark hex', () => {
    // Imported at top of file
    const { getContrastTextColor } = leaveTypeModels;
    expect(getContrastTextColor('#000000')).toBe('#ffffff');
    expect(getContrastTextColor('#2563eb')).toBe('#ffffff');
  });

  it('getContrastTextColor should return black for light hex', () => {
    const { getContrastTextColor } = leaveTypeModels;
    expect(getContrastTextColor('#ffffff')).toBe('#000000');
    expect(getContrastTextColor('#f0f0f0')).toBe('#000000');
  });
});
