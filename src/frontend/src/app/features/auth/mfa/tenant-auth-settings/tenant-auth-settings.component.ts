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
  FormControl,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';

@Component({
  selector: 'app-tenant-auth-settings',
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
          <h2 class="settings-title">Authentication Settings</h2>
          <p class="settings-subtitle">
            Configure multi-factor authentication policy for your organization.
          </p>
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading settings...</p>
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
            <!-- MFA Policy -->
            <div class="form-section">
              <label class="label-notion" for="mfaPolicy">MFA Policy</label>
              <p class="field-hint">Choose how MFA is enforced for users in your organization.</p>
              <select
                id="mfaPolicy"
                formControlName="mfaPolicy"
                class="input-notion select-input"
                [attr.disabled]="isReadonly() ? '' : null"
              >
                <option value="off">Off - MFA is disabled</option>
                <option value="optional">Optional - Users can choose to enable MFA</option>
                <option value="required">Required - Users must enable MFA</option>
              </select>
            </div>

            <!-- MFA Required Roles -->
            @if (form.get('mfaPolicy')?.value === 'required') {
              <div class="form-section" [@fadeSlide]>
                <label class="label-notion">Required Roles</label>
                <p class="field-hint">
                  Specify which roles must have MFA enabled. Leave empty to require it for all roles.
                </p>

                <!-- Role chips -->
                <div class="chips-container">
                  @for (role of roles(); track role) {
                    <div class="chip">
                      <span class="chip-label">{{ role }}</span>
                      @if (!isReadonly()) {
                        <button
                          type="button"
                          class="chip-remove"
                          (click)="removeRole(role)"
                          [attr.aria-label]="'Remove ' + role"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                            <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                          </svg>
                        </button>
                      }
                    </div>
                  }
                </div>

                @if (!isReadonly()) {
                  <div class="add-role-row">
                    <input
                      type="text"
                      class="input-notion add-role-input"
                      placeholder="Type a role and press Enter"
                      [formControl]="newRoleControl"
                      (keydown.enter)="addRole($event)"
                      (keydown.,)="addRole($event)"
                    />
                  </div>
                }
              </div>
            }

            <!-- Save button -->
            @if (!isReadonly()) {
              <div class="form-actions">
                <button
                  type="submit"
                  class="btn-primary"
                  [disabled]="isSaving() || !form.dirty"
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
                  Only Tenant Admins can modify authentication settings.
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
      @apply w-7 h-7 border-3 border-neutral-200 border-t-brand-600 rounded-full;
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

    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    .chips-container {
      @apply flex flex-wrap gap-2;
    }

    .chip {
      @apply inline-flex items-center gap-1 px-3 py-1 rounded-full
        bg-brand-50 text-brand-700 text-sm font-medium;
    }

    .chip-remove {
      @apply p-0.5 rounded-full hover:bg-brand-100 transition-colors
        text-brand-500 hover:text-brand-700 bg-transparent border-none cursor-pointer;
    }

    .add-role-row {
      @apply mt-1;
    }

    .add-role-input {
      @apply max-w-xs;
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
export class TenantAuthSettingsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);

  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly isSaving = signal(false);
  readonly isReadonly = signal(false);
  readonly roles = signal<string[]>([]);

  form!: FormGroup;
  newRoleControl = new FormControl('');

  ngOnInit(): void {
    this.form = this.fb.group({
      mfaPolicy: ['off'],
    });

    // Check if user has admin role
    this.isReadonly.set(!this.authService.hasRole('Tenant Admin'));

    this.loadSettings();
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.getTenantAuthSettings().subscribe({
      next: (settings: ITenantAuthSettings) => {
        this.form.patchValue({ mfaPolicy: settings.mfaPolicy });
        this.roles.set(settings.mfaRequiredRoles ?? []);
        this.form.markAsPristine();
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load authentication settings.'
        );
      },
    });
  }

  addRole(event: Event): void {
    event.preventDefault();
    const value = this.newRoleControl.value?.trim();
    if (value && !this.roles().includes(value)) {
      this.roles.update((r) => [...r, value]);
      this.form.markAsDirty();
    }
    this.newRoleControl.reset('');
  }

  removeRole(role: string): void {
    this.roles.update((r) => r.filter((item) => item !== role));
    this.form.markAsDirty();
  }

  onSave(): void {
    if (this.isReadonly() || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);

    const settings: ITenantAuthSettings = {
      mfaPolicy: this.form.value.mfaPolicy,
      mfaRequiredRoles: this.roles(),
    };

    this.authService.updateTenantAuthSettings(settings).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.form.markAsPristine();
        this.toastr.success('Authentication settings saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.toastr.error(
          err.error?.message || 'Failed to save settings. Please try again.'
        );
      },
    });
  }
}
