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
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * DF-27(c) / US-AUTH-004: authenticated self-service change-password.
 *
 * Rendered inside the authenticated shell (MainLayout), so the parent authGuard
 * already satisfies the endpoint's `[Authorize]`. Posts to
 * `POST /api/v1/auth/change-password` via the auth service. The backend verifies
 * the current password and enforces the tenant password policy + history rules,
 * returning a descriptive message for each failure (invalid current password,
 * password reuse, policy violations) which we surface verbatim.
 */
@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="settings-container" [@fadeSlide]>
      <div class="settings-card">
        <div class="settings-header">
          <h2 class="settings-title">Change password</h2>
          <p class="settings-subtitle">
            Update the password you use to sign in. Choose a strong password you
            don't use elsewhere.
          </p>
        </div>

        <div class="settings-body">
          @if (errorMessage()) {
            <div class="error-alert" role="alert">
              <span>{{ errorMessage() }}</span>
            </div>
          }

          <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form-section">
            <div class="form-group">
              <label for="currentPassword" class="label-notion">
                Current password
              </label>
              <input
                id="currentPassword"
                type="password"
                formControlName="currentPassword"
                class="input-notion"
                placeholder="Enter your current password"
                autocomplete="current-password"
              />
              @if (
                form.get('currentPassword')?.hasError('required') &&
                form.get('currentPassword')?.touched
              ) {
                <p class="field-error">Current password is required</p>
              }
            </div>

            <div class="form-group">
              <label for="newPassword" class="label-notion">New password</label>
              <input
                id="newPassword"
                type="password"
                formControlName="newPassword"
                class="input-notion"
                placeholder="Enter a new password"
                autocomplete="new-password"
              />

              <!-- Password strength checklist -->
              <div class="password-checklist">
                @for (rule of passwordRules(); track rule.label) {
                  <div
                    class="rule"
                    [class.rule-pass]="rule.pass"
                    [class.rule-fail]="!rule.pass && form.get('newPassword')?.dirty"
                  >
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
              @if (
                form.get('newPassword')?.hasError('sameAsCurrent') &&
                form.get('newPassword')?.touched
              ) {
                <p class="field-error">
                  The new password must be different from your current password
                </p>
              }
            </div>

            <div class="form-group">
              <label for="confirmPassword" class="label-notion">
                Confirm new password
              </label>
              <input
                id="confirmPassword"
                type="password"
                formControlName="confirmPassword"
                class="input-notion"
                placeholder="Re-enter your new password"
                autocomplete="new-password"
              />
              @if (
                form.get('confirmPassword')?.hasError('required') &&
                form.get('confirmPassword')?.touched
              ) {
                <p class="field-error">Please confirm your new password</p>
              }
              @if (
                form.hasError('passwordMismatch') &&
                form.get('confirmPassword')?.touched
              ) {
                <p class="field-error">Passwords do not match</p>
              }
            </div>

            <div class="actions">
              <button
                type="button"
                class="btn-secondary"
                (click)="cancel()"
                [disabled]="isLoading()"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="btn-primary"
                [disabled]="form.invalid || isLoading()"
              >
                @if (isLoading()) {
                  <span class="btn-spinner"></span>
                  Updating...
                } @else {
                  Update password
                }
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
    }

    .settings-container {
      @apply mx-auto max-w-2xl px-4 py-6 sm:px-6;
    }

    .settings-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion overflow-hidden;
    }

    .settings-header {
      @apply px-6 py-5 border-b border-neutral-50;
    }

    .settings-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .settings-subtitle {
      @apply mt-1 text-sm text-neutral-500;
    }

    .settings-body {
      @apply px-6 py-5;
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
        px-4 py-3 mb-5 text-sm text-red-700;
    }

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

    .actions {
      @apply flex justify-end gap-3 pt-1;
    }

    .btn-primary {
      @apply inline-flex items-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-secondary {
      @apply inline-flex items-center gap-2 rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-1 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class ChangePasswordComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');

  form!: FormGroup;

  /** Password rules for the strength checklist (mirrors the backend policy). */
  readonly passwordRules = signal([
    { label: 'At least 12 characters', pass: false },
    { label: 'One uppercase letter', pass: false },
    { label: 'One lowercase letter', pass: false },
    { label: 'One number', pass: false },
    { label: 'One special character', pass: false },
  ]);

  ngOnInit(): void {
    this.form = this.fb.group(
      {
        currentPassword: ['', [Validators.required]],
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
        validators: [this.passwordMatchValidator, this.newDiffersFromCurrent],
      }
    );

    this.form.get('newPassword')?.valueChanges.subscribe((pwd: string) => {
      const value = pwd ?? '';
      this.passwordRules.set([
        { label: 'At least 12 characters', pass: value.length >= 12 },
        { label: 'One uppercase letter', pass: /[A-Z]/.test(value) },
        { label: 'One lowercase letter', pass: /[a-z]/.test(value) },
        { label: 'One number', pass: /\d/.test(value) },
        {
          label: 'One special character',
          pass: /[!@#$%^&*(),.?":{}|<>]/.test(value),
        },
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

    const { currentPassword, newPassword } = this.form.value;

    this.authService
      .changePassword({ currentPassword, newPassword })
      .subscribe({
        next: () => {
          this.isLoading.set(false);
          this.toastr.success('Your password has been updated.');
          this.router.navigate(['/dashboard']);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          // The backend returns a descriptive message for every rejection
          // (invalid current password, password reuse, policy failure). Surface
          // it verbatim; fall back to a generic line otherwise.
          if (err.status === 400) {
            this.errorMessage.set(
              err.error?.message ||
                'Could not update your password. Please check your entries and try again.'
            );
          } else {
            this.errorMessage.set(
              'An unexpected error occurred. Please try again.'
            );
          }
        },
      });
  }

  cancel(): void {
    this.router.navigate(['/dashboard']);
  }

  /** Custom validator for password strength (matches the backend complexity rules). */
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

  /** Cross-field validator: new password and confirm must match. */
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

  /** Cross-field guard: the new password must differ from the current one (BR mirror). */
  private newDiffersFromCurrent(
    group: AbstractControl
  ): ValidationErrors | null {
    const current = group.get('currentPassword')?.value;
    const next = group.get('newPassword')?.value;
    const newControl = group.get('newPassword');

    if (current && next && current === next) {
      newControl?.setErrors({ ...(newControl.errors ?? {}), sameAsCurrent: true });
      return { sameAsCurrent: true };
    }

    // Clear only our own error so we don't stomp strength/length errors.
    if (newControl?.hasError('sameAsCurrent')) {
      const { sameAsCurrent, ...rest } = newControl.errors ?? {};
      newControl.setErrors(Object.keys(rest).length ? rest : null);
    }

    return null;
  }
}
