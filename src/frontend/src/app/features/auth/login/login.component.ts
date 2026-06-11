import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import { ILoginErrorResponse } from '../../../core/auth/auth.models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
  ],
})
export class LoginComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly errorMessage = signal('');
  readonly showPassword = signal(false);

  /**
   * US-AUTH-010 AC-2/AC-3: Distinguishes lockout errors from generic auth errors
   * so the template can render a distinct lockout banner.
   */
  readonly isAccountLocked = signal(false);

  togglePassword(): void {
    this.showPassword.update((v) => !v);
  }

  loginForm!: FormGroup;
  mfaForm!: FormGroup;

  ngOnInit(): void {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]],
    });

    this.mfaForm = this.fb.group({
      mfaCode: [
        '',
        [Validators.required, Validators.pattern(/^\d{6}$/)],
      ],
    });
  }

  onLoginSubmit(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');
    this.isAccountLocked.set(false);

    const { email, password } = this.loginForm.value;

    this.authService
      .login({ email: email.trim().toLowerCase(), password })
      .subscribe({
        next: (response) => {
          if (response.mfaChallenge) {
            this.authService.loginEmail.set(email.trim().toLowerCase());
            if (this.authService.mfaRequiresEnrollment()) {
              this.router.navigate(['/auth/mfa/enroll']);
            } else {
              this.router.navigate(['/auth/mfa/challenge']);
            }
          } else {
            this.router.navigate(['/dashboard']);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.handleLoginError(err);
        },
      });
  }

  onMfaSubmit(): void {
    if (this.mfaForm.invalid) {
      this.mfaForm.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');

    const { email, password } = this.loginForm.value;
    const { mfaCode } = this.mfaForm.value;

    this.authService
      .login({
        email: email.trim().toLowerCase(),
        password,
        mfaCode,
      })
      .subscribe({
        next: () => {
          this.router.navigate(['/dashboard']);
        },
        error: (err: HttpErrorResponse) => {
          this.handleLoginError(err);
        },
      });
  }

  backToLogin(): void {
    this.authService.mfaChallenge.set(false);
    this.mfaForm.reset();
    this.errorMessage.set('');
  }

  private handleLoginError(err: HttpErrorResponse): void {
    if (err.status === 401) {
      const body = err.error as ILoginErrorResponse | undefined;

      // US-AUTH-010 AC-2/AC-3: Detect lockout by the error code returned from
      // the backend. Show a distinct lockout banner instead of the generic
      // "Invalid email or password" message.
      if (body?.code === 'account_locked') {
        this.isAccountLocked.set(true);
        const minutes = body.lockoutMinutesRemaining;
        if (minutes && minutes > 0) {
          this.errorMessage.set(
            `Your account has been temporarily locked due to too many failed login attempts. Please try again in ${minutes} minute${minutes === 1 ? '' : 's'} or contact your administrator.`
          );
        } else {
          this.errorMessage.set(
            'Your account has been temporarily locked due to too many failed login attempts. Please try again later or contact your administrator.'
          );
        }
      } else {
        this.isAccountLocked.set(false);
        const message =
          body?.message || 'Invalid email or password.';
        this.errorMessage.set(message);
      }
    } else if (err.status === 403) {
      const message =
        err.error?.message ||
        'Access denied. Your account may be inactive or the workspace is unavailable.';
      this.errorMessage.set(message);
    } else if (err.status === 404) {
      this.errorMessage.set(
        'This workspace does not exist. Please check the URL.'
      );
    } else if (err.status === 429) {
      this.errorMessage.set(
        'Too many login attempts. Please wait a moment and try again.'
      );
    } else {
      this.errorMessage.set(
        'An unexpected error occurred. Please try again.'
      );
    }
  }
}
