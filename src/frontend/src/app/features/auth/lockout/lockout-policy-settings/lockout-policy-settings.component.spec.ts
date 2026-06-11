import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LockoutPolicySettingsComponent } from './lockout-policy-settings.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';

describe('LockoutPolicySettingsComponent', () => {
  let component: LockoutPolicySettingsComponent;
  let fixture: ComponentFixture<LockoutPolicySettingsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockSettings: ITenantAuthSettings = {
    mfaPolicy: 'optional',
    mfaRequiredRoles: [],
    idleTimeoutMinutes: 30,
    absoluteTimeoutHours: 12,
    maxConcurrentSessions: 3,
    concurrentSessionStrategy: 'revoke_oldest',
    maxFailedAttempts: 5,
    lockoutDurationMinutes: 15,
    progressiveLockoutEnabled: false,
  };

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getTenantAuthSettings',
      'updateTenantAuthSettings',
      'hasRole',
    ]);
    authServiceSpy.getTenantAuthSettings.and.returnValue(of(mockSettings));
    authServiceSpy.updateTenantAuthSettings.and.returnValue(of(undefined));
    authServiceSpy.hasRole.and.returnValue(true);

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [LockoutPolicySettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LockoutPolicySettingsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load settings on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getTenantAuthSettings).toHaveBeenCalled();
    expect(component.form.value.maxFailedAttempts).toBe(5);
    expect(component.form.value.lockoutDurationMinutes).toBe(15);
    expect(component.form.value.progressiveLockoutEnabled).toBe(false);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error when loading fails', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
  });

  it('should use defaults when settings are missing lockout fields', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({
        mfaPolicy: 'off',
        mfaRequiredRoles: [],
      } as ITenantAuthSettings)
    );
    fixture.detectChanges();
    expect(component.form.value.maxFailedAttempts).toBe(5);
    expect(component.form.value.lockoutDurationMinutes).toBe(15);
    expect(component.form.value.progressiveLockoutEnabled).toBe(false);
  });

  // ─── BR-5 Validation: maxFailedAttempts 3-10 ─────────────

  it('should validate maxFailedAttempts minimum (3)', () => {
    fixture.detectChanges();
    const control = component.form.get('maxFailedAttempts')!;

    control.setValue(2);
    expect(control.valid).toBeFalse();

    control.setValue(3);
    expect(control.valid).toBeTrue();
  });

  it('should validate maxFailedAttempts maximum (10)', () => {
    fixture.detectChanges();
    const control = component.form.get('maxFailedAttempts')!;

    control.setValue(10);
    expect(control.valid).toBeTrue();

    control.setValue(11);
    expect(control.valid).toBeFalse();
  });

  // ─── BR-5 Validation: lockoutDurationMinutes 5-60 ────────

  it('should validate lockoutDurationMinutes minimum (5)', () => {
    fixture.detectChanges();
    const control = component.form.get('lockoutDurationMinutes')!;

    control.setValue(4);
    expect(control.valid).toBeFalse();

    control.setValue(5);
    expect(control.valid).toBeTrue();
  });

  it('should validate lockoutDurationMinutes maximum (60)', () => {
    fixture.detectChanges();
    const control = component.form.get('lockoutDurationMinutes')!;

    control.setValue(60);
    expect(control.valid).toBeTrue();

    control.setValue(61);
    expect(control.valid).toBeFalse();
  });

  // ─── Save ─────────────────────────────────────────────────

  it('should save settings and merge with existing settings', () => {
    fixture.detectChanges();

    component.form.patchValue({
      maxFailedAttempts: 7,
      lockoutDurationMinutes: 30,
      progressiveLockoutEnabled: true,
    });
    component.form.markAsDirty();

    component.onSave();

    expect(authServiceSpy.updateTenantAuthSettings).toHaveBeenCalledWith(
      jasmine.objectContaining({
        mfaPolicy: 'optional',
        mfaRequiredRoles: [],
        idleTimeoutMinutes: 30,
        maxFailedAttempts: 7,
        lockoutDurationMinutes: 30,
        progressiveLockoutEnabled: true,
      })
    );
    expect(toastrSpy.success).toHaveBeenCalledWith('Lockout policy saved.');
    expect(component.isSaving()).toBeFalse();
  });

  it('should not save when form is invalid', () => {
    fixture.detectChanges();
    component.form.get('maxFailedAttempts')!.setValue(0);
    component.form.markAsDirty();

    component.onSave();

    expect(authServiceSpy.updateTenantAuthSettings).not.toHaveBeenCalled();
  });

  it('should handle save errors', () => {
    authServiceSpy.updateTenantAuthSettings.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Save failed' },
      }))
    );
    fixture.detectChanges();
    component.form.markAsDirty();
    component.onSave();

    expect(component.isSaving()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalledWith('Save failed');
  });

  // ─── Read-only ────────────────────────────────────────────

  it('should set readonly for non-admin users', () => {
    authServiceSpy.hasRole.and.returnValue(false);
    fixture.detectChanges();
    expect(component.isReadonly()).toBeTrue();
  });

  it('should not save when readonly', () => {
    authServiceSpy.hasRole.and.returnValue(false);
    fixture.detectChanges();
    component.form.markAsDirty();
    component.onSave();

    expect(authServiceSpy.updateTenantAuthSettings).not.toHaveBeenCalled();
  });
});
