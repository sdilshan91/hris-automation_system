import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { Subscription } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  IMfaEnrollResponse,
  MfaEnrollStep,
} from '../../../../core/auth/auth.models';

@Component({
  selector: 'app-mfa-enroll',
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
    <div class="enroll-card" [@fadeSlide]>
      <!-- Loading state -->
      @if (isLoading()) {
        <div class="loading-container">
          <div class="spinner"></div>
          <p class="loading-text">Setting up two-factor authentication...</p>
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div class="error-section">
          <div class="error-icon-wrapper">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-7 h-7 text-red-500">
              <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
            </svg>
          </div>
          <h3 class="error-title">Unable to start enrollment</h3>
          <p class="error-message">{{ loadError() }}</p>
          <button class="btn-primary mt-4" (click)="retryEnroll()">
            Try again
          </button>
        </div>
      }

      @if (data() && !isLoading() && !loadError()) {
        <!-- Step 1: QR Code -->
        @if (currentStep() === 'qr') {
          <div class="step-content" [@fadeSlide]>
            <div class="step-header">
              <div class="step-icon-wrapper">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-7 h-7 text-brand-600">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M3.75 4.875c0-.621.504-1.125 1.125-1.125h4.5c.621 0 1.125.504 1.125 1.125v4.5c0 .621-.504 1.125-1.125 1.125h-4.5A1.125 1.125 0 0 1 3.75 9.375v-4.5ZM3.75 14.625c0-.621.504-1.125 1.125-1.125h4.5c.621 0 1.125.504 1.125 1.125v4.5c0 .621-.504 1.125-1.125 1.125h-4.5a1.125 1.125 0 0 1-1.125-1.125v-4.5ZM13.5 4.875c0-.621.504-1.125 1.125-1.125h4.5c.621 0 1.125.504 1.125 1.125v4.5c0 .621-.504 1.125-1.125 1.125h-4.5A1.125 1.125 0 0 1 13.5 9.375v-4.5Z" />
                  <path stroke-linecap="round" stroke-linejoin="round" d="M6.75 6.75h.75v.75h-.75v-.75ZM6.75 16.5h.75v.75h-.75v-.75ZM16.5 6.75h.75v.75h-.75v-.75ZM13.5 13.5h.75v.75h-.75v-.75ZM13.5 19.5h.75v.75h-.75v-.75ZM19.5 13.5h.75v.75h-.75v-.75ZM19.5 19.5h.75v.75h-.75v-.75ZM16.5 16.5h.75v.75h-.75v-.75Z" />
                </svg>
              </div>
              <h2 class="step-title">Scan QR code</h2>
              <p class="step-subtitle">
                Open your authenticator app and scan this QR code to add your account.
              </p>
            </div>

            <div class="qr-container">
              <img
                [src]="data()!.qrCodeDataUrl"
                alt="Scan this QR code with your authenticator app"
                class="qr-image"
              />
            </div>

            <div class="secret-section">
              <p class="secret-label">Can't scan? Enter this code manually:</p>
              <div class="secret-card">
                <code class="secret-value">{{ data()!.secret }}</code>
                <button
                  class="copy-btn"
                  (click)="copySecret()"
                  [attr.aria-label]="'Copy secret key'"
                >
                  @if (secretCopied()) {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-green-600">
                      <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd" />
                    </svg>
                  } @else {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                      <path d="M7 3.5A1.5 1.5 0 0 1 8.5 2h3.879a1.5 1.5 0 0 1 1.06.44l3.122 3.12A1.5 1.5 0 0 1 17 6.622V12.5a1.5 1.5 0 0 1-1.5 1.5h-1v-3.379a3 3 0 0 0-.879-2.121L10.5 5.379A3 3 0 0 0 8.379 4.5H7v-1Z" />
                      <path d="M4.5 6A1.5 1.5 0 0 0 3 7.5v9A1.5 1.5 0 0 0 4.5 18h7a1.5 1.5 0 0 0 1.5-1.5v-5.879a1.5 1.5 0 0 0-.44-1.06L9.44 6.439A1.5 1.5 0 0 0 8.378 6H4.5Z" />
                    </svg>
                  }
                </button>
              </div>
            </div>

            <button
              class="btn-primary w-full mt-6"
              (click)="currentStep.set('verify')"
            >
              I've scanned it, continue
            </button>
          </div>
        }

        <!-- Step 2: Verify -->
        @if (currentStep() === 'verify') {
          <div class="step-content" [@fadeSlide]>
            <div class="step-header">
              <div class="step-icon-wrapper">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-7 h-7 text-brand-600">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z" />
                </svg>
              </div>
              <h2 class="step-title">Verify setup</h2>
              <p class="step-subtitle">
                Enter the 6-digit code from your authenticator app to confirm setup.
              </p>
            </div>

            @if (verifyError()) {
              <div class="error-alert" role="alert">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 flex-shrink-0 text-red-500">
                  <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
                </svg>
                <span>{{ verifyError() }}</span>
              </div>
            }

            <form [formGroup]="verifyForm" (ngSubmit)="onVerifySubmit()" class="verify-form">
              <div class="form-group">
                <label for="totpCode" class="label-notion">Verification code</label>
                <input
                  id="totpCode"
                  type="text"
                  formControlName="code"
                  class="input-notion totp-input"
                  placeholder="000000"
                  maxlength="6"
                  autocomplete="one-time-code"
                  inputmode="numeric"
                  autofocus
                />
              </div>

              <button
                type="submit"
                class="btn-primary w-full"
                [disabled]="verifyForm.invalid || isVerifying()"
              >
                @if (isVerifying()) {
                  <span class="btn-spinner"></span>
                  Verifying...
                } @else {
                  Verify and activate
                }
              </button>
            </form>

            <button
              class="back-link"
              (click)="currentStep.set('qr')"
            >
              Back to QR code
            </button>
          </div>
        }

        <!-- Step 3: Recovery codes -->
        @if (currentStep() === 'recovery') {
          <div class="step-content" [@fadeSlide]>
            <div class="step-header">
              <div class="step-icon-wrapper success">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-7 h-7 text-green-600">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
                </svg>
              </div>
              <h2 class="step-title">MFA enabled</h2>
              <p class="step-subtitle">
                Two-factor authentication has been set up successfully.
              </p>
            </div>

            <div class="warning-banner">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 flex-shrink-0 text-amber-600">
                <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
              </svg>
              <div>
                <p class="warning-title">Save these recovery codes</p>
                <p class="warning-text">
                  Store them in a safe place. They cannot be shown again. Each code can only be used once.
                </p>
              </div>
            </div>

            <div class="recovery-codes-grid">
              @for (code of recoveryCodes(); track code) {
                <div class="recovery-code-chip">{{ code }}</div>
              }
            </div>

            <div class="recovery-actions">
              <button class="btn-secondary" (click)="copyAllCodes()">
                @if (codesCopied()) {
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-green-600">
                    <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd" />
                  </svg>
                  Copied
                } @else {
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                    <path d="M7 3.5A1.5 1.5 0 0 1 8.5 2h3.879a1.5 1.5 0 0 1 1.06.44l3.122 3.12A1.5 1.5 0 0 1 17 6.622V12.5a1.5 1.5 0 0 1-1.5 1.5h-1v-3.379a3 3 0 0 0-.879-2.121L10.5 5.379A3 3 0 0 0 8.379 4.5H7v-1Z" />
                    <path d="M4.5 6A1.5 1.5 0 0 0 3 7.5v9A1.5 1.5 0 0 0 4.5 18h7a1.5 1.5 0 0 0 1.5-1.5v-5.879a1.5 1.5 0 0 0-.44-1.06L9.44 6.439A1.5 1.5 0 0 0 8.378 6H4.5Z" />
                  </svg>
                  Copy all
                }
              </button>
              <button class="btn-secondary" (click)="downloadCodes()">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                  <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z" />
                  <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z" />
                </svg>
                Download .txt
              </button>
            </div>

            <button
              class="btn-primary w-full mt-6"
              (click)="finish()"
            >
              I've saved them, finish
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
      width: 100%;
    }

    .enroll-card {
      @apply w-full rounded-xl bg-white border border-neutral-100 p-8 shadow-notion;
      @apply sm:p-10;
    }

    .loading-container {
      @apply flex flex-col items-center justify-center py-12;
    }

    .spinner {
      @apply w-8 h-8 border-2 border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    .loading-text {
      @apply mt-4 text-sm text-neutral-500;
    }

    .error-section {
      @apply text-center py-6;
    }

    .error-icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-red-50 flex items-center justify-center;
    }

    .error-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .error-message {
      @apply mt-1 text-sm text-neutral-500;
    }

    .step-content {
      @apply space-y-6;
    }

    .step-header {
      @apply text-center;
    }

    .step-icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-brand-50 flex items-center justify-center;
    }

    .step-icon-wrapper.success {
      @apply bg-green-50;
    }

    .step-title {
      @apply text-xl font-semibold text-neutral-900 tracking-tight;
    }

    .step-subtitle {
      @apply mt-1.5 text-sm text-neutral-500;
    }

    .qr-container {
      @apply flex justify-center;
    }

    .qr-image {
      @apply w-48 h-48 sm:w-56 sm:h-56 rounded-lg border border-neutral-100;
      max-width: 220px;
    }

    .secret-section {
      @apply text-center;
    }

    .secret-label {
      @apply text-xs text-neutral-400 mb-2;
    }

    .secret-card {
      @apply inline-flex items-center gap-2 rounded-lg bg-neutral-50 border border-neutral-200
        px-4 py-2.5;
    }

    .secret-value {
      @apply text-sm font-mono text-neutral-700 tracking-wider select-all break-all;
    }

    .copy-btn {
      @apply flex-shrink-0 p-1.5 rounded-md text-neutral-400 hover:text-neutral-600
        hover:bg-neutral-200 transition-colors;
    }

    .error-alert {
      @apply flex items-center gap-2 rounded-lg bg-red-50 border border-red-100
        px-4 py-3 text-sm text-red-700;
    }

    .verify-form {
      @apply space-y-5;
    }

    .form-group {
      @apply space-y-1;
    }

    .totp-input {
      @apply text-center text-2xl font-mono tracking-[0.5em];
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    .back-link {
      @apply block w-full text-center text-sm text-neutral-500
        hover:text-neutral-700 transition-colors mt-4 cursor-pointer
        bg-transparent border-none;
    }

    .warning-banner {
      @apply flex gap-3 rounded-lg bg-amber-50 border border-amber-200 px-4 py-3;
    }

    .warning-title {
      @apply text-sm font-medium text-amber-800;
    }

    .warning-text {
      @apply text-xs text-amber-700 mt-0.5;
    }

    .recovery-codes-grid {
      @apply grid grid-cols-2 gap-2;
    }

    .recovery-code-chip {
      @apply px-3 py-2 rounded-lg bg-neutral-50 border border-neutral-200
        text-sm font-mono text-neutral-700 text-center select-all;
    }

    .recovery-actions {
      @apply flex gap-3;
    }

    .recovery-actions .btn-secondary {
      @apply flex-1 inline-flex items-center justify-center gap-2 rounded-lg
        bg-white px-4 py-2.5 text-sm font-medium text-neutral-700 shadow-sm
        ring-1 ring-inset ring-neutral-200 transition-all duration-200
        hover:bg-neutral-50;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class MfaEnrollComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly currentStep = signal<MfaEnrollStep>('qr');
  readonly data = signal<IMfaEnrollResponse | null>(null);
  readonly recoveryCodes = signal<string[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly isVerifying = signal(false);
  readonly verifyError = signal('');
  readonly secretCopied = signal(false);
  readonly codesCopied = signal(false);

  verifyForm!: FormGroup;
  private codeChangeSub?: Subscription;

  ngOnInit(): void {
    this.verifyForm = this.fb.group({
      code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    });

    // Auto-submit when 6 digits are entered
    this.codeChangeSub = this.verifyForm
      .get('code')
      ?.valueChanges.subscribe((value: string) => {
        if (value && /^\d{6}$/.test(value)) {
          this.onVerifySubmit();
        }
      });

    this.startEnrollment();
  }

  ngOnDestroy(): void {
    this.codeChangeSub?.unsubscribe();
  }

  startEnrollment(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.enrollMfa().subscribe({
      next: (response) => {
        this.data.set(response);
        this.recoveryCodes.set(response.recoveryCodes ?? []);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to start MFA enrollment. Please try again.'
        );
      },
    });
  }

  retryEnroll(): void {
    this.startEnrollment();
  }

  onVerifySubmit(): void {
    if (this.verifyForm.invalid || this.isVerifying()) {
      return;
    }

    this.isVerifying.set(true);
    this.verifyError.set('');

    const code = this.verifyForm.value.code;

    this.authService.verifyMfaEnrollment(code).subscribe({
      next: (response) => {
        this.isVerifying.set(false);
        if (response.success) {
          if (response.recoveryCodes?.length) {
            this.recoveryCodes.set(response.recoveryCodes);
          }
          this.currentStep.set('recovery');
        } else {
          this.verifyError.set('Invalid verification code. Please try again.');
          this.verifyForm.get('code')?.reset('');
        }
      },
      error: (err: HttpErrorResponse) => {
        this.isVerifying.set(false);
        this.verifyError.set(
          err.error?.message || 'Invalid verification code. Please try again.'
        );
        this.verifyForm.get('code')?.reset('');
      },
    });
  }

  copySecret(): void {
    const secret = this.data()?.secret;
    if (secret) {
      navigator.clipboard.writeText(secret).then(() => {
        this.secretCopied.set(true);
        setTimeout(() => this.secretCopied.set(false), 2000);
      });
    }
  }

  copyAllCodes(): void {
    const codes = this.recoveryCodes().join('\n');
    navigator.clipboard.writeText(codes).then(() => {
      this.codesCopied.set(true);
      setTimeout(() => this.codesCopied.set(false), 2000);
    });
  }

  downloadCodes(): void {
    const codes = this.recoveryCodes().join('\n');
    const blob = new Blob(
      ['YourHRM Recovery Codes\n', '======================\n\n', codes, '\n'],
      { type: 'text/plain' }
    );
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'yourhrm-recovery-codes.txt';
    a.click();
    URL.revokeObjectURL(url);
  }

  finish(): void {
    this.router.navigate(['/dashboard']);
  }
}
