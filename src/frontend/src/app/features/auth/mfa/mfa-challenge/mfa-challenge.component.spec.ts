import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { signal } from '@angular/core';
import { ToastrService } from 'ngx-toastr';

import { MfaChallengeComponent } from './mfa-challenge.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ILoginResponse } from '../../../../core/auth/auth.models';

describe('MfaChallengeComponent', () => {
  let component: MfaChallengeComponent;
  let fixture: ComponentFixture<MfaChallengeComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  const mockLoginResponse: ILoginResponse = {
    accessToken: 'test-token',
    user: {
      userId: '1',
      email: 'test@example.com',
      displayName: 'Test User',
      mfaEnabled: true,
    },
    tenant: {
      tenantId: 't1',
      subdomain: 'test',
      name: 'Test Tenant',
      status: 'active',
    },
    permissions: ['Dashboard.View'],
  };

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj(
      'AuthService',
      ['verifyMfaLogin', 'cancelMfaChallenge'],
      {
        loginEmail: signal('test@example.com'),
        mfaChallenge: signal(true),
      }
    );
    authServiceSpy.verifyMfaLogin.and.returnValue(of(mockLoginResponse));

    await TestBed.configureTestingModule({
      imports: [MfaChallengeComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MfaChallengeComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should start in TOTP mode', () => {
    expect(component.useRecoveryCode()).toBeFalse();
  });

  it('should submit TOTP code and navigate to dashboard on success', () => {
    const navigateSpy = spyOn(router, 'navigate');
    component.totpForm.patchValue({ code: '123456' });
    component.onTotpSubmit();
    expect(authServiceSpy.verifyMfaLogin).toHaveBeenCalledWith(
      'test@example.com',
      '123456'
    );
    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });

  it('should switch to recovery code mode', () => {
    component.switchToRecovery();
    expect(component.useRecoveryCode()).toBeTrue();
  });

  it('should switch back to TOTP mode', () => {
    component.switchToRecovery();
    component.switchToTotp();
    expect(component.useRecoveryCode()).toBeFalse();
  });

  it('should submit recovery code', () => {
    const navigateSpy = spyOn(router, 'navigate');
    component.switchToRecovery();
    component.recoveryForm.patchValue({ code: 'code-1111-aa' });
    component.onRecoverySubmit();
    expect(authServiceSpy.verifyMfaLogin).toHaveBeenCalledWith(
      'test@example.com',
      'code-1111-aa'
    );
    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });

  it('should show error on 401', () => {
    authServiceSpy.verifyMfaLogin.and.returnValue(
      throwError(() => ({
        status: 401,
        error: { message: 'Invalid verification code.' },
      }))
    );
    component.totpForm.patchValue({ code: '000000' });
    component.onTotpSubmit();
    expect(component.errorMessage()).toBe('Invalid verification code.');
  });

  it('should show locked message on 429', () => {
    authServiceSpy.verifyMfaLogin.and.returnValue(
      throwError(() => ({
        status: 429,
        error: { message: 'Too many attempts' },
      }))
    );
    component.totpForm.patchValue({ code: '000000' });
    component.onTotpSubmit();
    expect(component.errorMessage()).toContain('temporarily locked');
  });

  // DF-27(b): a recovery-code login that returns shouldRegenerateRecoveryCodes
  // (or a low remaining count) must warn the user to regenerate their codes.
  it('should warn to regenerate recovery codes when the backend flags it (DF-27b)', () => {
    const toastr = TestBed.inject(ToastrService);
    const warnSpy = spyOn(toastr, 'warning');
    authServiceSpy.verifyMfaLogin.and.returnValue(
      of({
        ...mockLoginResponse,
        recoveryCodesRemaining: 2,
        shouldRegenerateRecoveryCodes: true,
      })
    );
    spyOn(router, 'navigate');

    component.switchToRecovery();
    component.recoveryForm.patchValue({ code: 'code-1111-aa' });
    component.onRecoverySubmit();

    expect(warnSpy).toHaveBeenCalled();
    expect(warnSpy.calls.mostRecent().args[0]).toContain('2 recovery codes left');
  });

  it('should NOT warn on a normal TOTP login (DF-27b)', () => {
    const toastr = TestBed.inject(ToastrService);
    const warnSpy = spyOn(toastr, 'warning');
    authServiceSpy.verifyMfaLogin.and.returnValue(
      of({ ...mockLoginResponse, shouldRegenerateRecoveryCodes: true })
    );
    spyOn(router, 'navigate');

    component.totpForm.patchValue({ code: '123456' });
    component.onTotpSubmit();

    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('should call cancelMfaChallenge and navigate back to login', () => {
    const navigateSpy = spyOn(router, 'navigate');
    component.backToLogin();
    expect(authServiceSpy.cancelMfaChallenge).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/auth/login']);
  });
});
