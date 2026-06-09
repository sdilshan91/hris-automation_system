import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { Router } from '@angular/router';

import { MfaEnrollComponent } from './mfa-enroll.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { IMfaEnrollResponse, IMfaVerifyResponse } from '../../../../core/auth/auth.models';

describe('MfaEnrollComponent', () => {
  let component: MfaEnrollComponent;
  let fixture: ComponentFixture<MfaEnrollComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  const mockEnrollResponse: IMfaEnrollResponse = {
    secret: 'JBSWY3DPEHPK3PXP',
    qrCodeDataUrl: 'data:image/png;base64,abc123',
    recoveryCodes: ['code-1111-aa', 'code-2222-bb', 'code-3333-cc', 'code-4444-dd', 'code-5555-ee'],
  };

  const mockVerifySuccessResponse: IMfaVerifyResponse = {
    success: true,
    recoveryCodes: ['code-1111-aa', 'code-2222-bb', 'code-3333-cc'],
  };

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'enrollMfa',
      'verifyMfaEnrollment',
    ]);
    authServiceSpy.enrollMfa.and.returnValue(of(mockEnrollResponse));
    authServiceSpy.verifyMfaEnrollment.and.returnValue(of(mockVerifySuccessResponse));

    await TestBed.configureTestingModule({
      imports: [MfaEnrollComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MfaEnrollComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should call enrollMfa on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.enrollMfa).toHaveBeenCalledOnceWith();
  });

  it('should set data signal after successful enrollment', () => {
    fixture.detectChanges();
    expect(component.data()).toEqual(mockEnrollResponse);
    expect(component.isLoading()).toBeFalse();
    expect(component.currentStep()).toBe('qr');
  });

  it('should show load error when enrollment fails', () => {
    authServiceSpy.enrollMfa.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Server error' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should transition from qr to verify step', () => {
    fixture.detectChanges();
    expect(component.currentStep()).toBe('qr');
    component.currentStep.set('verify');
    expect(component.currentStep()).toBe('verify');
  });

  it('should call verifyMfaEnrollment on verify submit', () => {
    fixture.detectChanges();
    component.currentStep.set('verify');
    component.verifyForm.patchValue({ code: '123456' });
    component.onVerifySubmit();
    expect(authServiceSpy.verifyMfaEnrollment).toHaveBeenCalledWith('123456');
  });

  it('should transition to recovery step on successful verification', () => {
    fixture.detectChanges();
    component.currentStep.set('verify');
    component.verifyForm.patchValue({ code: '123456' });
    component.onVerifySubmit();
    expect(component.currentStep()).toBe('recovery');
  });

  it('should show error on failed verification', () => {
    authServiceSpy.verifyMfaEnrollment.and.returnValue(
      throwError(() => ({
        status: 400,
        error: { message: 'Invalid verification code.' },
      }))
    );
    fixture.detectChanges();
    component.currentStep.set('verify');
    component.verifyForm.patchValue({ code: '123456' });
    component.onVerifySubmit();
    expect(component.verifyError()).toBe('Invalid verification code.');
  });

  it('should navigate to dashboard on finish', () => {
    const navigateSpy = spyOn(router, 'navigate');
    fixture.detectChanges();
    component.finish();
    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });
});
