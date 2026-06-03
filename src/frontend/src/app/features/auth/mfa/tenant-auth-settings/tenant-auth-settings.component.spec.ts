import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { TenantAuthSettingsComponent } from './tenant-auth-settings.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';

describe('TenantAuthSettingsComponent', () => {
  let component: TenantAuthSettingsComponent;
  let fixture: ComponentFixture<TenantAuthSettingsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  const mockSettings: ITenantAuthSettings = {
    mfaPolicy: 'optional',
    mfaRequiredRoles: ['Tenant Admin'],
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

    await TestBed.configureTestingModule({
      imports: [TenantAuthSettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TenantAuthSettingsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load settings on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getTenantAuthSettings).toHaveBeenCalled();
    expect(component.form.value.mfaPolicy).toBe('optional');
    expect(component.roles()).toEqual(['Tenant Admin']);
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

  it('should validate mfaPolicy values', () => {
    fixture.detectChanges();
    component.form.patchValue({ mfaPolicy: 'required' });
    expect(component.form.value.mfaPolicy).toBe('required');

    component.form.patchValue({ mfaPolicy: 'off' });
    expect(component.form.value.mfaPolicy).toBe('off');
  });

  it('should add roles', () => {
    fixture.detectChanges();
    component.newRoleControl.setValue('HR Officer');
    const event = new Event('keydown');
    event.preventDefault = jasmine.createSpy('preventDefault');
    component.addRole(event);
    expect(component.roles()).toContain('HR Officer');
  });

  it('should remove roles', () => {
    fixture.detectChanges();
    component.removeRole('Tenant Admin');
    expect(component.roles()).not.toContain('Tenant Admin');
  });

  it('should not add duplicate roles', () => {
    fixture.detectChanges();
    const initialLength = component.roles().length;
    component.newRoleControl.setValue('Tenant Admin');
    const event = new Event('keydown');
    event.preventDefault = jasmine.createSpy('preventDefault');
    component.addRole(event);
    expect(component.roles().length).toBe(initialLength);
  });

  it('should save settings', () => {
    fixture.detectChanges();
    component.form.patchValue({ mfaPolicy: 'required' });
    component.form.markAsDirty();
    component.onSave();
    expect(authServiceSpy.updateTenantAuthSettings).toHaveBeenCalledWith({
      mfaPolicy: 'required',
      mfaRequiredRoles: ['Tenant Admin'],
    });
  });

  it('should set readonly for non-admin users', () => {
    authServiceSpy.hasRole.and.returnValue(false);
    fixture.detectChanges();
    expect(component.isReadonly()).toBeTrue();
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
  });
});
