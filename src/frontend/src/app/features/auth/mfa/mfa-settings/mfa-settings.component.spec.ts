import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { signal, computed } from '@angular/core';

import { MfaSettingsComponent } from './mfa-settings.component';
import { AuthService } from '../../../../core/auth/auth.service';

describe('MfaSettingsComponent', () => {
  let component: MfaSettingsComponent;
  let fixture: ComponentFixture<MfaSettingsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  function createComponent(mfaEnabled: boolean): void {
    const mfaEnabledSignal = signal(mfaEnabled);
    authServiceSpy = jasmine.createSpyObj(
      'AuthService',
      ['disableMfa'],
      {
        mfaEnabled: computed(() => mfaEnabledSignal()),
        currentUser: signal({
          userId: '1',
          email: 'test@example.com',
          displayName: 'Test User',
          mfaEnabled,
        }),
      }
    );
    authServiceSpy.disableMfa.and.returnValue(of(undefined));

    TestBed.overrideProvider(AuthService, { useValue: authServiceSpy });

    fixture = TestBed.createComponent(MfaSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [MfaSettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: {} },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
  });

  it('should create with MFA enabled', () => {
    createComponent(true);
    expect(component).toBeTruthy();
  });

  it('should create with MFA disabled', () => {
    createComponent(false);
    expect(component).toBeTruthy();
  });

  it('should show confirm dialog when disable is clicked', () => {
    createComponent(true);
    expect(component.showConfirmDialog()).toBeFalse();
    component.confirmDisable();
    expect(component.showConfirmDialog()).toBeTrue();
  });

  it('should hide confirm dialog when cancel is clicked', () => {
    createComponent(true);
    component.confirmDisable();
    component.cancelDisable();
    expect(component.showConfirmDialog()).toBeFalse();
  });

  it('should call disableMfa on confirm', () => {
    createComponent(true);
    component.disableMfa();
    expect(authServiceSpy.disableMfa).toHaveBeenCalled();
  });

  it('should handle 403 error when disabling MFA', () => {
    createComponent(true);
    authServiceSpy.disableMfa.and.returnValue(
      throwError(() => ({
        status: 403,
        error: { message: 'Policy requires MFA' },
      }))
    );
    component.disableMfa();
    expect(component.isDisabling()).toBeFalse();
    expect(component.showConfirmDialog()).toBeFalse();
  });

  it('should navigate to enroll when enable is clicked', () => {
    createComponent(false);
    const navigateSpy = spyOn(router, 'navigate');
    component.enableMfa();
    expect(navigateSpy).toHaveBeenCalledWith(['/auth/mfa/enroll']);
  });
});
