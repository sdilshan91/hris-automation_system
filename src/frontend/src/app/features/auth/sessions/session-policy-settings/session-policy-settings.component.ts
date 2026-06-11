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
 * US-AUTH-009 FR-1: Session policy settings for Tenant Admin > Security Settings.
 * Configures idleTimeoutMinutes, absoluteTimeoutHours, maxConcurrentSessions,
 * and concurrentSessionStrategy (deny_new | revoke_oldest).
 */
@Component({
  selector: 'app-session-policy-settings',
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
          <h2 class="settings-title">Session Policy</h2>
          <p class="settings-subtitle">
            Configure session timeouts and concurrent session limits for your organization.
          </p>
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading session policy...</p>
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
            <!-- Idle Timeout -->
            <div class="form-section">
              <label class="label-notion" for="idleTimeoutMinutes">
                Idle timeout (minutes)
              </label>
              <p class="field-hint">
                Sessions expire after this many minutes of inactivity.
                Users will see a warning 5 minutes before expiry.
              </p>
              <input
                id="idleTimeoutMinutes"
                type="number"
                formControlName="idleTimeoutMinutes"
                class="input-notion max-w-xs"
                min="5"
                max="1440"
                [attr.readonly]="isReadonly() ? '' : null"
              />
              @if (form.get('idleTimeoutMinutes')?.invalid && form.get('idleTimeoutMinutes')?.touched) {
                <p class="field-error">Must be between 5 and 1440 minutes.</p>
              }
            </div>

            <!-- Absolute Timeout -->
            <div class="form-section">
              <label class="label-notion" for="absoluteTimeoutHours">
                Absolute timeout (hours)
              </label>
              <p class="field-hint">
                Sessions expire after this many hours regardless of activity.
                Requires re-authentication.
              </p>
              <input
                id="absoluteTimeoutHours"
                type="number"
                formControlName="absoluteTimeoutHours"
                class="input-notion max-w-xs"
                min="1"
                max="720"
                [attr.readonly]="isReadonly() ? '' : null"
              />
              @if (form.get('absoluteTimeoutHours')?.invalid && form.get('absoluteTimeoutHours')?.touched) {
                <p class="field-error">Must be between 1 and 720 hours.</p>
              }
            </div>

            <!-- Max Concurrent Sessions -->
            <div class="form-section">
              <label class="label-notion" for="maxConcurrentSessions">
                Maximum concurrent sessions
              </label>
              <p class="field-hint">
                The maximum number of active sessions a user can have at the same time.
              </p>
              <input
                id="maxConcurrentSessions"
                type="number"
                formControlName="maxConcurrentSessions"
                class="input-notion max-w-xs"
                min="1"
                max="100"
                [attr.readonly]="isReadonly() ? '' : null"
              />
              @if (form.get('maxConcurrentSessions')?.invalid && form.get('maxConcurrentSessions')?.touched) {
                <p class="field-error">Must be between 1 and 100.</p>
              }
            </div>

            <!-- Concurrent Session Strategy -->
            <div class="form-section">
              <label class="label-notion" for="concurrentSessionStrategy">
                When session limit is reached
              </label>
              <p class="field-hint">
                Choose what happens when a user tries to log in but already has the
                maximum number of active sessions.
              </p>
              <select
                id="concurrentSessionStrategy"
                formControlName="concurrentSessionStrategy"
                class="input-notion select-input max-w-sm"
                [attr.disabled]="isReadonly() ? '' : null"
              >
                <option value="deny_new">
                  Deny new login
                </option>
                <option value="revoke_oldest">
                  Revoke the oldest session
                </option>
              </select>
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
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-neutral-400">
                  <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
                </svg>
                <span class="text-sm text-neutral-500">
                  Only Tenant Admins can modify session policies.
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

    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    .form-actions {
      @apply pt-2;
    }

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
export class SessionPolicySettingsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);

  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly isSaving = signal(false);
  readonly isReadonly = signal(false);

  form!: FormGroup;

  /** Full settings reference so we can merge back the MFA fields on save */
  private currentSettings: ITenantAuthSettings | null = null;

  ngOnInit(): void {
    this.form = this.fb.group({
      idleTimeoutMinutes: [60, [Validators.required, Validators.min(5), Validators.max(1440)]],
      absoluteTimeoutHours: [24, [Validators.required, Validators.min(1), Validators.max(720)]],
      maxConcurrentSessions: [5, [Validators.required, Validators.min(1), Validators.max(100)]],
      concurrentSessionStrategy: ['deny_new', Validators.required],
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
          idleTimeoutMinutes: settings.idleTimeoutMinutes ?? 60,
          absoluteTimeoutHours: settings.absoluteTimeoutHours ?? 24,
          maxConcurrentSessions: settings.maxConcurrentSessions ?? 5,
          concurrentSessionStrategy: settings.concurrentSessionStrategy ?? 'deny_new',
        });
        this.form.markAsPristine();
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load session policy settings.'
        );
      },
    });
  }

  onSave(): void {
    if (this.isReadonly() || this.isSaving() || this.form.invalid) {
      return;
    }

    this.isSaving.set(true);

    // Merge session policy fields with existing MFA settings
    const updatedSettings: ITenantAuthSettings = {
      ...(this.currentSettings as ITenantAuthSettings),
      ...this.form.value,
    };

    this.authService.updateTenantAuthSettings(updatedSettings).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.form.markAsPristine();
        this.currentSettings = updatedSettings;
        this.toastr.success('Session policy saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.toastr.error(
          err.error?.message || 'Failed to save session policy. Please try again.'
        );
      },
    });
  }
}
