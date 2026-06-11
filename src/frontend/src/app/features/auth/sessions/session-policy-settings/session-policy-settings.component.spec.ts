import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { SessionPolicySettingsComponent } from './session-policy-settings.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';

describe('SessionPolicySettingsComponent', () => {
  let component: SessionPolicySettingsComponent;
  let fixture: ComponentFixture<SessionPolicySettingsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockSettings: ITenantAuthSettings = {
    mfaPolicy: 'optional',
    mfaRequiredRoles: [],
    idleTimeoutMinutes: 30,
    absoluteTimeoutHours: 12,
    maxConcurrentSessions: 3,
    concurrentSessionStrategy: 'revoke_oldest',
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
      imports: [SessionPolicySettingsComponent],
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

    fixture = TestBed.createComponent(SessionPolicySettingsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load settings on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getTenantAuthSettings).toHaveBeenCalled();
    expect(component.form.value.idleTimeoutMinutes).toBe(30);
    expect(component.form.value.absoluteTimeoutHours).toBe(12);
    expect(component.form.value.maxConcurrentSessions).toBe(3);
    expect(component.form.value.concurrentSessionStrategy).toBe(
      'revoke_oldest'
    );
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

  it('should use defaults when settings are missing session fields', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({
        mfaPolicy: 'off',
        mfaRequiredRoles: [],
      } as ITenantAuthSettings)
    );
    fixture.detectChanges();
    expect(component.form.value.idleTimeoutMinutes).toBe(60);
    expect(component.form.value.absoluteTimeoutHours).toBe(24);
    expect(component.form.value.maxConcurrentSessions).toBe(5);
    expect(component.form.value.concurrentSessionStrategy).toBe('deny_new');
  });

  it('should validate idleTimeoutMinutes range', () => {
    fixture.detectChanges();
    const control = component.form.get('idleTimeoutMinutes')!;

    control.setValue(3);
    expect(control.valid).toBeFalse();

    control.setValue(5);
    expect(control.valid).toBeTrue();

    control.setValue(1440);
    expect(control.valid).toBeTrue();

    control.setValue(1441);
    expect(control.valid).toBeFalse();
  });

  it('should validate absoluteTimeoutHours range', () => {
    fixture.detectChanges();
    const control = component.form.get('absoluteTimeoutHours')!;

    control.setValue(0);
    expect(control.valid).toBeFalse();

    control.setValue(1);
    expect(control.valid).toBeTrue();

    control.setValue(720);
    expect(control.valid).toBeTrue();

    control.setValue(721);
    expect(control.valid).toBeFalse();
  });

  it('should validate maxConcurrentSessions range', () => {
    fixture.detectChanges();
    const control = component.form.get('maxConcurrentSessions')!;

    control.setValue(0);
    expect(control.valid).toBeFalse();

    control.setValue(1);
    expect(control.valid).toBeTrue();

    control.setValue(100);
    expect(control.valid).toBeTrue();

    control.setValue(101);
    expect(control.valid).toBeFalse();
  });

  it('should save settings and merge with existing MFA settings', () => {
    fixture.detectChanges();

    component.form.patchValue({
      idleTimeoutMinutes: 45,
      maxConcurrentSessions: 10,
    });
    component.form.markAsDirty();

    component.onSave();

    expect(authServiceSpy.updateTenantAuthSettings).toHaveBeenCalledWith(
      jasmine.objectContaining({
        mfaPolicy: 'optional',
        mfaRequiredRoles: [],
        idleTimeoutMinutes: 45,
        absoluteTimeoutHours: 12,
        maxConcurrentSessions: 10,
        concurrentSessionStrategy: 'revoke_oldest',
      })
    );
    expect(toastrSpy.success).toHaveBeenCalledWith('Session policy saved.');
    expect(component.isSaving()).toBeFalse();
  });

  it('should not save when form is invalid', () => {
    fixture.detectChanges();
    component.form.get('idleTimeoutMinutes')!.setValue(0);
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
