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
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';

@Component({
  selector: 'app-mfa-challenge',
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
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
  ],
  template: `
    <div class="challenge-card" [@fadeSlide]>
      <div class="challenge-header">
        <div class="icon-wrapper">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" class="w-8 h-8 text-brand-600">
            <path stroke-linecap="round" stroke-linejoin="round" d="M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z" />
          </svg>
        </div>
        <h2 class="challenge-title">Two-factor authentication</h2>
        <p class="challenge-subtitle">
          @if (useRecoveryCode()) {
            Enter one of your recovery codes
          } @else {
            Enter the 6-digit code from your authenticator app
          }
        </p>
      </div>

      @if (errorMessage()) {
        <div class="error-alert" role="alert" [@fadeIn]>
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 flex-shrink-0 text-red-500">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
          </svg>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      <!-- TOTP code form -->
      @if (!useRecoveryCode()) {
        <form [formGroup]="totpForm" (ngSubmit)="onTotpSubmit()" class="challenge-form" [@fadeSlide]>
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
            @if (totpForm.get('code')?.hasError('pattern') && totpForm.get('code')?.touched) {
              <p class="field-error">Code must be 6 digits</p>
            }
          </div>

          <button
            type="submit"
            class="btn-primary w-full"
            [disabled]="totpForm.invalid || isSubmitting()"
          >
            @if (isSubmitting()) {
              <span class="btn-spinner"></span>
              Verifying...
            } @else {
              Verify
            }
          </button>
        </form>

        <button
          class="toggle-link"
          (click)="switchToRecovery()"
        >
          Use a recovery code instead
        </button>
      } @else {
        <!-- Recovery code form -->
        <form [formGroup]="recoveryForm" (ngSubmit)="onRecoverySubmit()" class="challenge-form" [@fadeSlide]>
          <div class="form-group">
            <label for="recoveryCode" class="label-notion">Recovery code</label>
            <input
              id="recoveryCode"
              type="text"
              formControlName="code"
              class="input-notion recovery-input"
              placeholder="xxxx-xxxx-xx"
              maxlength="12"
              autocomplete="off"
              autofocus
            />
          </div>

          <button
            type="submit"
            class="btn-primary w-full"
            [disabled]="recoveryForm.invalid || isSubmitting()"
          >
            @if (isSubmitting()) {
              <span class="btn-spinner"></span>
              Verifying...
            } @else {
              Verify recovery code
            }
          </button>
        </form>

        <button
          class="toggle-link"
          (click)="switchToTotp()"
        >
          Use authenticator code instead
        </button>
      }

      <button
        class="back-link"
        (click)="backToLogin()"
      >
        Back to login
      </button>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
      width: 100%;
    }

    .challenge-card {
      @apply w-full rounded-xl bg-white border border-neutral-100 p-8 shadow-notion;
      @apply sm:p-10;
    }

    .challenge-header {
      @apply text-center mb-6;
    }

    .icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-brand-50 flex items-center justify-center;
    }

    .challenge-title {
      @apply text-xl font-semibold text-neutral-900 tracking-tight;
    }

    .challenge-subtitle {
      @apply mt-1.5 text-sm text-neutral-500;
    }

    .error-alert {
      @apply flex items-center gap-2 rounded-lg bg-red-50 border border-red-100
        px-4 py-3 mb-6 text-sm text-red-700;
    }

    .challenge-form {
      @apply space-y-5;
    }

    .form-group {
      @apply space-y-1;
    }

    .totp-input {
      @apply text-center text-2xl font-mono tracking-[0.5em];
    }

    .recovery-input {
      @apply text-center text-lg font-mono tracking-wider;
    }

    .field-error {
      @apply text-xs text-red-500 mt-1;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    .toggle-link {
      @apply block w-full text-center text-sm text-brand-600
        hover:text-brand-700 transition-colors mt-4 cursor-pointer
        bg-transparent border-none font-medium;
    }

    .back-link {
      @apply block w-full text-center text-sm text-neutral-500
        hover:text-neutral-700 transition-colors mt-3 cursor-pointer
        bg-transparent border-none;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class MfaChallengeComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly useRecoveryCode = signal(false);
  readonly errorMessage = signal('');
  readonly isSubmitting = signal(false);

  totpForm!: FormGroup;
  recoveryForm!: FormGroup;

  private totpCodeSub?: Subscription;
  private recoveryCodeSub?: Subscription;

  ngOnInit(): void {
    this.totpForm = this.fb.group({
      code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    });

    this.recoveryForm = this.fb.group({
      code: ['', [Validators.required, Validators.minLength(8)]],
    });

    // Auto-submit when 6 digits are entered for TOTP
    this.totpCodeSub = this.totpForm
      .get('code')
      ?.valueChanges.subscribe((value: string) => {
        if (value && /^\d{6}$/.test(value)) {
          this.onTotpSubmit();
        }
      });

    // Auto-submit recovery code at 11 chars (format: xxxx-xxxx-xx)
    this.recoveryCodeSub = this.recoveryForm
      .get('code')
      ?.valueChanges.subscribe((value: string) => {
        if (value && value.length >= 11) {
          this.onRecoverySubmit();
        }
      });
  }

  ngOnDestroy(): void {
    this.totpCodeSub?.unsubscribe();
    this.recoveryCodeSub?.unsubscribe();
  }

  onTotpSubmit(): void {
    if (this.totpForm.invalid || this.isSubmitting()) {
      return;
    }
    this.submitCode(this.totpForm.value.code);
  }

  onRecoverySubmit(): void {
    if (this.recoveryForm.invalid || this.isSubmitting()) {
      return;
    }
    this.submitCode(this.recoveryForm.value.code);
  }

  switchToRecovery(): void {
    this.useRecoveryCode.set(true);
    this.errorMessage.set('');
    this.totpForm.reset();
  }

  switchToTotp(): void {
    this.useRecoveryCode.set(false);
    this.errorMessage.set('');
    this.recoveryForm.reset();
  }

  backToLogin(): void {
    this.authService.cancelMfaChallenge();
    this.router.navigate(['/auth/login']);
  }

  private submitCode(code: string): void {
    this.isSubmitting.set(true);
    this.errorMessage.set('');

    const email = this.authService.loginEmail();

    this.authService.verifyMfaLogin(email, code).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/dashboard']);
      },
      error: (err: HttpErrorResponse) => {
        this.isSubmitting.set(false);
        const message = err.error?.message || 'Verification failed.';

        if (err.status === 429) {
          this.errorMessage.set(
            'Account temporarily locked due to too many failed attempts. Please try again later.'
          );
          this.toastr.error('Account temporarily locked.');
        } else if (err.status === 401) {
          this.errorMessage.set(message);
        } else {
          this.errorMessage.set('An unexpected error occurred. Please try again.');
          this.toastr.error('An unexpected error occurred.');
        }

        // Reset the active form
        if (this.useRecoveryCode()) {
          this.recoveryForm.get('code')?.reset('');
        } else {
          this.totpForm.get('code')?.reset('');
        }
      },
    });
  }
}
