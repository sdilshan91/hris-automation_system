import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ChangePasswordComponent } from './change-password.component';
import { AuthService } from '../../../core/auth/auth.service';

describe('ChangePasswordComponent (DF-27c)', () => {
  let component: ChangePasswordComponent;
  let fixture: ComponentFixture<ChangePasswordComponent>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const STRONG = 'NewStr0ng!Pass1';

  beforeEach(async () => {
    authSpy = jasmine.createSpyObj('AuthService', ['changePassword']);
    authSpy.changePassword.and.returnValue(of({ message: 'ok' }));
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ChangePasswordComponent],
      providers: [
        provideRouter([]),
        provideAnimationsAsync(),
        { provide: AuthService, useValue: authSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ChangePasswordComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('blocks submit while the form is invalid', () => {
    component.onSubmit();
    expect(authSpy.changePassword).not.toHaveBeenCalled();
  });

  it('flags a new password that matches the current one', () => {
    component.form.patchValue({
      currentPassword: STRONG,
      newPassword: STRONG,
      confirmPassword: STRONG,
    });
    expect(component.form.get('newPassword')?.hasError('sameAsCurrent')).toBeTrue();
    component.onSubmit();
    expect(authSpy.changePassword).not.toHaveBeenCalled();
  });

  it('flags mismatched confirm password', () => {
    component.form.patchValue({
      currentPassword: 'OldStr0ng!Pass1',
      newPassword: STRONG,
      confirmPassword: 'Different1!AAaa',
    });
    expect(component.form.hasError('passwordMismatch')).toBeTrue();
  });

  it('posts the current + new password and toasts success', () => {
    const navSpy = spyOn(router, 'navigate');
    component.form.patchValue({
      currentPassword: 'OldStr0ng!Pass1',
      newPassword: STRONG,
      confirmPassword: STRONG,
    });
    component.onSubmit();
    expect(authSpy.changePassword).toHaveBeenCalledWith({
      currentPassword: 'OldStr0ng!Pass1',
      newPassword: STRONG,
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith(['/dashboard']);
  });

  it('surfaces the backend 400 message verbatim (invalid_current_password / password_reused / policy)', () => {
    authSpy.changePassword.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { message: 'Your current password is incorrect.' },
            status: 400,
          })
      )
    );
    component.form.patchValue({
      currentPassword: 'WrongStr0ng!1A',
      newPassword: STRONG,
      confirmPassword: STRONG,
    });
    component.onSubmit();
    expect(component.errorMessage()).toBe('Your current password is incorrect.');
  });
});
