import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';

@Component({
  selector: 'app-mfa-settings',
  standalone: true,
  imports: [CommonModule],
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
          <div class="header-content">
            <h2 class="settings-title">Two-factor authentication</h2>
            <p class="settings-subtitle">
              Add an extra layer of security to your account using a time-based one-time password (TOTP).
            </p>
          </div>
        </div>

        <div class="settings-body">
          @if (authService.mfaEnabled()) {
            <!-- MFA is enabled -->
            <div class="status-section enabled">
              <div class="status-badge enabled">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                  <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
                </svg>
                <span>MFA enabled</span>
              </div>
              <p class="status-text">
                Your account is protected with two-factor authentication.
              </p>
            </div>

            <div class="actions-section">
              <button
                class="btn-secondary"
                (click)="regenerateRecoveryCodes()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                  <path fill-rule="evenodd" d="M15.312 11.424a5.5 5.5 0 0 1-9.201 2.466l-.312-.311h2.433a.75.75 0 0 0 0-1.5H4.638a.75.75 0 0 0-.75.75v3.594a.75.75 0 0 0 1.5 0v-2.187l.312.311a7 7 0 0 0 11.712-3.138.75.75 0 0 0-1.449-.39Zm1.023-7.18a.75.75 0 0 0-1.5 0v2.187l-.312-.31a7 7 0 0 0-11.712 3.137.75.75 0 0 0 1.449.39 5.5 5.5 0 0 1 9.201-2.466l.312.311H11.34a.75.75 0 1 0 0 1.5h3.594a.75.75 0 0 0 .75-.75V4.244Z" clip-rule="evenodd" />
                </svg>
                Regenerate recovery codes
              </button>
              <button
                class="btn-danger"
                (click)="confirmDisable()"
                [disabled]="isDisabling()"
              >
                @if (isDisabling()) {
                  <span class="btn-spinner-sm"></span>
                  Disabling...
                } @else {
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                    <path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16ZM8.28 7.22a.75.75 0 0 0-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 1 0 1.06 1.06L10 11.06l1.72 1.72a.75.75 0 1 0 1.06-1.06L11.06 10l1.72-1.72a.75.75 0 0 0-1.06-1.06L10 8.94 8.28 7.22Z" clip-rule="evenodd" />
                  </svg>
                  Disable MFA
                }
              </button>
            </div>

            <!-- Confirm disable dialog -->
            @if (showConfirmDialog()) {
              <div class="confirm-overlay" (click)="cancelDisable()">
                <div class="confirm-dialog" (click)="$event.stopPropagation()" [@fadeSlide]>
                  <div class="confirm-icon-wrapper">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-7 h-7 text-amber-600">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
                    </svg>
                  </div>
                  <h3 class="confirm-title">Disable two-factor authentication?</h3>
                  <p class="confirm-text">
                    This will remove the extra security layer from your account. You can re-enable it at any time.
                  </p>
                  <div class="confirm-actions">
                    <button class="btn-secondary" (click)="cancelDisable()">
                      Cancel
                    </button>
                    <button
                      class="btn-danger"
                      (click)="disableMfa()"
                      [disabled]="isDisabling()"
                    >
                      @if (isDisabling()) {
                        <span class="btn-spinner-sm"></span>
                      }
                      Yes, disable MFA
                    </button>
                  </div>
                </div>
              </div>
            }
          } @else {
            <!-- MFA is not enabled -->
            <div class="status-section disabled">
              <div class="status-badge disabled">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                  <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
                </svg>
                <span>MFA not enabled</span>
              </div>
              <p class="status-text">
                Protect your account by adding two-factor authentication.
              </p>
            </div>

            <button
              class="btn-primary"
              (click)="enableMfa()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
              </svg>
              Enable two-factor authentication
            </button>
          }
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
      @apply px-6 py-5 space-y-5;
    }

    .status-section {
      @apply space-y-2;
    }

    .status-badge {
      @apply inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium;
    }

    .status-badge.enabled {
      @apply bg-green-50 text-green-700;
    }

    .status-badge.disabled {
      @apply bg-neutral-100 text-neutral-600;
    }

    .status-text {
      @apply text-sm text-neutral-600;
    }

    .actions-section {
      @apply flex flex-wrap gap-3 pt-2;
    }

    .btn-secondary {
      @apply inline-flex items-center gap-2 rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-danger {
      @apply inline-flex items-center gap-2 rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-red-600 shadow-sm ring-1 ring-inset ring-red-200
        transition-all duration-200 hover:bg-red-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-primary {
      @apply inline-flex items-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700;
    }

    .btn-spinner-sm {
      @apply inline-block w-3.5 h-3.5 border-2 border-current/30 border-t-current rounded-full;
      animation: spin 0.6s linear infinite;
    }

    /* Confirm dialog overlay */
    .confirm-overlay {
      @apply fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm px-4;
    }

    .confirm-dialog {
      @apply w-full max-w-sm rounded-xl bg-white border border-neutral-100 p-6 shadow-notion-lg text-center;
    }

    .confirm-icon-wrapper {
      @apply w-12 h-12 mx-auto mb-3 rounded-full bg-amber-50 flex items-center justify-center;
    }

    .confirm-title {
      @apply text-base font-semibold text-neutral-900;
    }

    .confirm-text {
      @apply mt-1.5 text-sm text-neutral-500;
    }

    .confirm-actions {
      @apply flex gap-3 mt-5;
    }

    .confirm-actions .btn-secondary,
    .confirm-actions .btn-danger {
      @apply flex-1 justify-center;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class MfaSettingsComponent {
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly showConfirmDialog = signal(false);
  readonly isDisabling = signal(false);

  enableMfa(): void {
    this.router.navigate(['/auth/mfa/enroll']);
  }

  regenerateRecoveryCodes(): void {
    this.router.navigate(['/auth/mfa/enroll']);
  }

  confirmDisable(): void {
    this.showConfirmDialog.set(true);
  }

  cancelDisable(): void {
    this.showConfirmDialog.set(false);
  }

  disableMfa(): void {
    this.isDisabling.set(true);

    this.authService.disableMfa().subscribe({
      next: () => {
        this.isDisabling.set(false);
        this.showConfirmDialog.set(false);
        this.toastr.success('Two-factor authentication has been disabled.');
      },
      error: (err: HttpErrorResponse) => {
        this.isDisabling.set(false);
        this.showConfirmDialog.set(false);

        if (err.status === 403) {
          this.toastr.error(
            'MFA is required by your organization policy and cannot be disabled.'
          );
        } else {
          this.toastr.error(
            err.error?.message || 'Failed to disable MFA. Please try again.'
          );
        }
      },
    });
  }
}
