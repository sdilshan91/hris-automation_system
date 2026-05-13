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
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="reset-card" [@fadeSlide]>
      @if (success()) {
        <!-- Success state -->
        <div class="success-section" [@fadeSlide]>
          <div class="success-icon-wrapper">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
              class="success-icon"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"
              />
            </svg>
          </div>
          <h2 class="card-title">Password updated</h2>
          <p class="card-subtitle">
            Your password has been successfully reset. Redirecting to login...
          </p>
          <a routerLink="/auth/login" class="btn-primary w-full mt-6 text-center no-underline">
            Sign in
          </a>
        </div>
      } @else if (invalidToken()) {
        <!-- Invalid/expired token -->
        <div class="error-section" [@fadeSlide]>
          <div class="error-icon-wrapper">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
              class="error-icon-lg"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z"
              />
            </svg>
          </div>
          <h2 class="card-title">Link expired</h2>
          <p class="card-subtitle">
            This password reset link has expired or has already been used.
            Please request a new one.
          </p>
          <a routerLink="/auth/forgot-password" class="btn-primary w-full mt-6 text-center no-underline">
            Request new link
          </a>
        </div>
      } @else {
        <!-- Reset form -->
        <div class="card-header">
          <h2 class="card-title">Set a new password</h2>
          <p class="card-subtitle">
            Choose a strong password for your account.
          </p>
        </div>

        @if (errorMessage()) {
          <div class="error-alert" role="alert">
            <span>{{ errorMessage() }}</span>
          </div>
        }

        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form-section">
          <div class="form-group">
            <label for="newPassword" class="label-notion">New password</label>
            <input
              id="newPassword"
              type="password"
              formControlName="newPassword"
              class="input-notion"
              placeholder="Enter new password"
              autocomplete="new-password"
              autofocus
            />

            <!-- Password strength checklist -->
            <div class="password-checklist">
              @for (rule of passwordRules(); track rule.label) {
                <div class="rule" [class.rule-pass]="rule.pass" [class.rule-fail]="!rule.pass && form.get('newPassword')?.dirty">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                    @if (rule.pass) {
                      <path fill-rule="evenodd" d="M12.416 3.376a.75.75 0 0 1 .208 1.04l-5 7.5a.75.75 0 0 1-1.154.114l-3-3a.75.75 0 0 1 1.06-1.06l2.353 2.353 4.493-6.74a.75.75 0 0 1 1.04-.207Z" clip-rule="evenodd"/>
                    } @else {
                      <circle cx="8" cy="8" r="2.5"/>
                    }
                  </svg>
                  <span>{{ rule.label }}</span>
                </div>
              }
            </div>
          </div>

          <div class="form-group">
            <label for="confirmPassword" class="label-notion">Confirm password</label>
            <input
              id="confirmPassword"
              type="password"
              formControlName="confirmPassword"
              class="input-notion"
              placeholder="Re-enter new password"
              autocomplete="new-password"
            />
            @if (form.get('confirmPassword')?.hasError('required') && form.get('confirmPassword')?.touched) {
              <p class="field-error">Please confirm your password</p>
            }
            @if (form.hasError('passwordMismatch') && form.get('confirmPassword')?.touched) {
              <p class="field-error">Passwords do not match</p>
            }
          </div>

          <button
            type="submit"
            class="btn-primary w-full"
            [disabled]="form.invalid || isLoading()"
          >
            @if (isLoading()) {
              <span class="btn-spinner"></span>
              Updating...
            } @else {
              Reset password
            }
          </button>
        </form>

        <a routerLink="/auth/login" class="back-link">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
            <path fill-rule="evenodd" d="M17 10a.75.75 0 0 1-.75.75H5.612l4.158 3.96a.75.75 0 1 1-1.04 1.08l-5.5-5.25a.75.75 0 0 1 0-1.08l5.5-5.25a.75.75 0 1 1 1.04 1.08L5.612 9.25H16.25A.75.75 0 0 1 17 10Z" clip-rule="evenodd"/>
          </svg>
          Back to sign in
        </a>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
    }

    .reset-card {
      @apply w-full rounded-xl bg-white border border-neutral-100 p-8 shadow-notion;
      @apply sm:p-10;
    }

    .card-header {
      @apply text-center mb-6;
    }

    .card-title {
      @apply text-2xl font-semibold text-neutral-900 tracking-tight;
    }

    .card-subtitle {
      @apply mt-1.5 text-sm text-neutral-500;
    }

    .form-section {
      @apply space-y-5;
    }

    .form-group {
      @apply space-y-1;
    }

    .field-error {
      @apply text-xs text-red-500 mt-1;
    }

    .error-alert {
      @apply flex items-center gap-2 rounded-lg bg-red-50 border border-red-100
        px-4 py-3 mb-6 text-sm text-red-700;
    }

    /* Password strength checklist */
    .password-checklist {
      @apply mt-3 space-y-1.5;
    }

    .rule {
      @apply flex items-center gap-2 text-xs text-neutral-400 transition-colors;
    }

    .rule-pass {
      @apply text-green-600;
    }

    .rule-fail {
      @apply text-red-400;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30
        border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .back-link {
      @apply flex items-center justify-center gap-1.5 mt-6 text-sm
        text-neutral-500 hover:text-neutral-700 transition-colors no-underline;
    }

    .success-section,
    .error-section {
      @apply text-center;
    }

    .success-icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-green-50
        flex items-center justify-center;
    }

    .success-icon {
      @apply w-7 h-7 text-green-600;
    }

    .error-icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-amber-50
        flex items-center justify-center;
    }

    .error-icon-lg {
      @apply w-7 h-7 text-amber-600;
    }
  `],
})
export class ResetPasswordComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly isLoading = signal(false);
  readonly success = signal(false);
  readonly invalidToken = signal(false);
  readonly errorMessage = signal('');

  form!: FormGroup;

  private token = '';
  private email = '';

  /** Password rules for the strength checklist */
  readonly passwordRules = signal([
    { label: 'At least 12 characters', pass: false },
    { label: 'One uppercase letter', pass: false },
    { label: 'One lowercase letter', pass: false },
    { label: 'One number', pass: false },
    { label: 'One special character', pass: false },
  ]);

  ngOnInit(): void {
    // Extract token and email from query params
    this.route.queryParams.subscribe((params) => {
      this.token = params['token'] || '';
      this.email = params['email'] || '';

      if (!this.token || !this.email) {
        this.invalidToken.set(true);
      }
    });

    this.form = this.fb.group(
      {
        newPassword: [
          '',
          [
            Validators.required,
            Validators.minLength(12),
            this.passwordStrengthValidator,
          ],
        ],
        confirmPassword: ['', [Validators.required]],
      },
      {
        validators: [this.passwordMatchValidator],
      }
    );

    // Subscribe to password changes to update the strength checklist
    this.form.get('newPassword')?.valueChanges.subscribe((pwd: string) => {
      this.passwordRules.set([
        { label: 'At least 12 characters', pass: pwd.length >= 12 },
        { label: 'One uppercase letter', pass: /[A-Z]/.test(pwd) },
        { label: 'One lowercase letter', pass: /[a-z]/.test(pwd) },
        { label: 'One number', pass: /\d/.test(pwd) },
        { label: 'One special character', pass: /[!@#$%^&*(),.?":{}|<>]/.test(pwd) },
      ]);
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    const { newPassword } = this.form.value;

    this.authService
      .resetPassword({
        email: this.email,
        token: this.token,
        newPassword,
      })
      .subscribe({
        next: () => {
          this.success.set(true);
          this.isLoading.set(false);

          // Auto-redirect to login after 3 seconds
          setTimeout(() => {
            this.router.navigate(['/auth/login']);
          }, 3000);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);

          if (err.status === 400) {
            const message =
              err.error?.message ||
              'The reset link has expired or is invalid. Please request a new one.';

            if (
              message.includes('expired') ||
              message.includes('invalid') ||
              message.includes('already been used')
            ) {
              this.invalidToken.set(true);
            } else {
              this.errorMessage.set(message);
            }
          } else {
            this.errorMessage.set(
              'An unexpected error occurred. Please try again.'
            );
          }
        },
      });
  }

  /** Custom validator for password strength */
  private passwordStrengthValidator(
    control: AbstractControl
  ): ValidationErrors | null {
    const value = control.value;
    if (!value) return null;

    const hasUpper = /[A-Z]/.test(value);
    const hasLower = /[a-z]/.test(value);
    const hasDigit = /\d/.test(value);
    const hasSpecial = /[!@#$%^&*(),.?":{}|<>]/.test(value);

    if (hasUpper && hasLower && hasDigit && hasSpecial) {
      return null;
    }

    return { passwordStrength: true };
  }

  /** Cross-field validator: password and confirm must match */
  private passwordMatchValidator(
    group: AbstractControl
  ): ValidationErrors | null {
    const password = group.get('newPassword')?.value;
    const confirm = group.get('confirmPassword')?.value;

    if (password && confirm && password !== confirm) {
      return { passwordMismatch: true };
    }

    return null;
  }
}
