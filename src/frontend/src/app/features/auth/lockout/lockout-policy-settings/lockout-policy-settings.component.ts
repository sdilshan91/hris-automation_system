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
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';

/**
 * US-AUTH-010 FR-3 / BR-5: Lockout policy settings for Tenant Admin > Security Settings.
 * Configures maxFailedAttempts (3-10), lockoutDurationMinutes (5-60), and
 * progressiveLockoutEnabled toggle.
 *
 * Follows the same pattern as SessionPolicySettingsComponent (US-AUTH-009):
 * loads full ITenantAuthSettings, patches the lockout-specific fields, and
 * merges them back on save so sibling MFA and session settings are preserved.
 */
@Component({
  selector: 'app-lockout-policy-settings',
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
          <h2 class="settings-title">Account Lockout Policy</h2>
          <p class="settings-subtitle">
            Configure how failed login attempts are handled to protect user accounts
            from brute-force attacks.
          </p>
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading lockout policy...</p>
          </div>
        } @else if (loadError()) {
          <div class="error-section">
            <p class="error-text">{{ loadError() }}</p>
            <button class="btn-secondary mt-3" (click)="loadSettings()">
              Retry
            </button>
          </div>
        } @else {
          <form [formGroup]="form" (ngSubmit)="onSave()" class="settings-body">
            <!-- Max Failed Attempts -->
            <div class="form-section">
              <label class="label-notion" for="maxFailedAttempts">
                Maximum failed attempts
              </label>
              <p class="field-hint">
                Number of consecutive failed login attempts before the account is
                temporarily locked. Must be between 3 and 10.
              </p>
              <input
                id="maxFailedAttempts"
                type="number"
                formControlName="maxFailedAttempts"
                class="input-notion max-w-xs"
                min="3"
                max="10"
                [attr.readonly]="isReadonly() ? '' : null"
              />
              @if (form.get('maxFailedAttempts')?.invalid && form.get('maxFailedAttempts')?.touched) {
                <p class="field-error">Must be between 3 and 10 attempts.</p>
              }
            </div>

            <!-- Lockout Duration -->
            <div class="form-section">
              <label class="label-notion" for="lockoutDurationMinutes">
                Lockout duration (minutes)
              </label>
              <p class="field-hint">
                How long an account stays locked after exceeding the failed attempt
                threshold. Must be between 5 and 60 minutes.
              </p>
              <input
                id="lockoutDurationMinutes"
                type="number"
                formControlName="lockoutDurationMinutes"
                class="input-notion max-w-xs"
                min="5"
                max="60"
                [attr.readonly]="isReadonly() ? '' : null"
              />
              @if (form.get('lockoutDurationMinutes')?.invalid && form.get('lockoutDurationMinutes')?.touched) {
                <p class="field-error">Must be between 5 and 60 minutes.</p>
              }
            </div>

            <!-- Progressive Lockout Toggle -->
            <div class="form-section">
              <div class="toggle-row">
                <div class="toggle-label-block">
                  <label class="label-notion" for="progressiveLockoutEnabled">
                    Progressive lockout
                  </label>
                  <p class="field-hint">
                    When enabled, repeated lockout cycles within 24 hours will
                    progressively double the lockout duration.
                  </p>
                </div>
                <label class="toggle-switch" for="progressiveLockoutEnabled">
                  <input
                    id="progressiveLockoutEnabled"
                    type="checkbox"
                    formControlName="progressiveLockoutEnabled"
                    class="toggle-input"
                    [attr.disabled]="isReadonly() ? '' : null"
                  />
                  <span class="toggle-slider"></span>
                </label>
              </div>
            </div>

            <!-- Save button -->
            @if (!isReadonly()) {
              <div class="form-actions">
                <button
                  type="submit"
                  class="btn-primary"
                  [disabled]="isSaving() || !form.dirty || form.invalid"
                >
                  @if (isSaving()) {
                    <span class="btn-spinner"></span>
                    Saving...
                  } @else {
                    Save changes
                  }
                </button>
              </div>
            } @else {
              <div class="readonly-notice">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-neutral-400" aria-hidden="true">
                  <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
                </svg>
                <span class="text-sm text-neutral-500">
                  Only Tenant Admins can modify lockout policies.
                </span>
              </div>
            }
          </form>
        }
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
      @apply px-6 py-5 space-y-6;
    }

    .loading-section {
      @apply flex flex-col items-center justify-center py-12;
    }

    .spinner {
      @apply w-7 h-7 border-2 border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    .loading-text {
      @apply mt-3 text-sm text-neutral-500;
    }

    .error-section {
      @apply px-6 py-8 text-center;
    }

    .error-text {
      @apply text-sm text-red-600;
    }

    .form-section {
      @apply space-y-2;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .field-error {
      @apply text-xs text-red-600 mt-1;
    }

    .form-actions {
      @apply pt-2;
    }

    /* ─── Toggle switch ─────────────────────────── */

    .toggle-row {
      @apply flex items-start justify-between gap-4;
    }

    .toggle-label-block {
      @apply flex-1;
    }

    .toggle-switch {
      @apply relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer
        rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out;
      background-color: theme('colors.neutral.200');
    }

    .toggle-input {
      @apply sr-only;
    }

    .toggle-input:checked + .toggle-slider {
      transform: translateX(1.25rem);
    }

    .toggle-input:checked ~ .toggle-slider,
    .toggle-switch:has(.toggle-input:checked) {
      /* handled via parent bg */
    }

    .toggle-switch:has(.toggle-input:checked) {
      background-color: theme('colors.brand.600');
    }

    .toggle-slider {
      @apply pointer-events-none inline-block h-5 w-5 transform rounded-full
        bg-white shadow ring-0 transition duration-200 ease-in-out;
    }

    /* ─── Buttons ───────────────────────────────── */

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    .readonly-notice {
      @apply flex items-center gap-2 rounded-lg bg-neutral-50 border border-neutral-200 px-4 py-3;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class LockoutPolicySettingsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);

  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly isSaving = signal(false);
  readonly isReadonly = signal(false);

  form!: FormGroup;

  /** Full settings reference so we can merge back the MFA + session fields on save */
  private currentSettings: ITenantAuthSettings | null = null;

  ngOnInit(): void {
    // BR-5: maxFailedAttempts 3-10, lockoutDurationMinutes 5-60
    this.form = this.fb.group({
      maxFailedAttempts: [
        5,
        [Validators.required, Validators.min(3), Validators.max(10)],
      ],
      lockoutDurationMinutes: [
        15,
        [Validators.required, Validators.min(5), Validators.max(60)],
      ],
      progressiveLockoutEnabled: [false],
    });

    this.isReadonly.set(!this.authService.hasRole('Tenant Admin'));
    this.loadSettings();
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.getTenantAuthSettings().subscribe({
      next: (settings: ITenantAuthSettings) => {
        this.currentSettings = settings;
        this.form.patchValue({
          maxFailedAttempts: settings.maxFailedAttempts ?? 5,
          lockoutDurationMinutes: settings.lockoutDurationMinutes ?? 15,
          progressiveLockoutEnabled: settings.progressiveLockoutEnabled ?? false,
        });
        this.form.markAsPristine();
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load lockout policy settings.'
        );
      },
    });
  }

  onSave(): void {
    if (this.isReadonly() || this.isSaving() || this.form.invalid) {
      return;
    }

    this.isSaving.set(true);

    // Merge lockout policy fields with existing MFA + session settings
    const updatedSettings: ITenantAuthSettings = {
      ...(this.currentSettings as ITenantAuthSettings),
      ...this.form.value,
    };

    this.authService.updateTenantAuthSettings(updatedSettings).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.form.markAsPristine();
        this.currentSettings = updatedSettings;
        this.toastr.success('Lockout policy saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.toastr.error(
          err.error?.message || 'Failed to save lockout policy. Please try again.'
        );
      },
    });
  }
}
