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
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-forgot-password',
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
    <div class="forgot-card" [@fadeSlide]>
      @if (!submitted()) {
        <!-- Request form -->
        <div class="card-header">
          <h2 class="card-title">Reset your password</h2>
          <p class="card-subtitle">
            Enter your email address and we'll send you a link to reset your password.
          </p>
        </div>

        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form-section">
          <div class="form-group">
            <label for="email" class="label-notion">Email address</label>
            <input
              id="email"
              type="email"
              formControlName="email"
              class="input-notion"
              placeholder="you@company.com"
              autocomplete="email"
              autofocus
            />
            @if (form.get('email')?.hasError('required') && form.get('email')?.touched) {
              <p class="field-error">Email is required</p>
            }
            @if (form.get('email')?.hasError('email') && form.get('email')?.touched) {
              <p class="field-error">Please enter a valid email address</p>
            }
          </div>

          <button
            type="submit"
            class="btn-primary w-full"
            [disabled]="form.invalid || isLoading()"
          >
            @if (isLoading()) {
              <span class="btn-spinner"></span>
              Sending...
            } @else {
              Send reset link
            }
          </button>
        </form>

        <a routerLink="/auth/login" class="back-link">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
            <path fill-rule="evenodd" d="M17 10a.75.75 0 0 1-.75.75H5.612l4.158 3.96a.75.75 0 1 1-1.04 1.08l-5.5-5.25a.75.75 0 0 1 0-1.08l5.5-5.25a.75.75 0 1 1 1.04 1.08L5.612 9.25H16.25A.75.75 0 0 1 17 10Z" clip-rule="evenodd"/>
          </svg>
          Back to sign in
        </a>
      } @else {
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
                d="M21.75 6.75v10.5a2.25 2.25 0 0 1-2.25 2.25h-15a2.25 2.25 0 0 1-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0 0 19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25m19.5 0v.243a2.25 2.25 0 0 1-1.07 1.916l-7.5 4.615a2.25 2.25 0 0 1-2.36 0L3.32 8.91a2.25 2.25 0 0 1-1.07-1.916V6.75"
              />
            </svg>
          </div>
          <h2 class="card-title">Check your email</h2>
          <p class="card-subtitle">
            If an account with that email exists, we've sent a password reset link.
            Please check your inbox.
          </p>
          <a routerLink="/auth/login" class="btn-secondary w-full mt-6 text-center no-underline">
            Back to sign in
          </a>
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
    }

    .forgot-card {
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

    .success-section {
      @apply text-center;
    }

    .success-icon-wrapper {
      @apply w-14 h-14 mx-auto mb-4 rounded-full bg-brand-50
        flex items-center justify-center;
    }

    .success-icon {
      @apply w-7 h-7 text-brand-600;
    }
  `],
})
export class ForgotPasswordComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  readonly isLoading = signal(false);
  readonly submitted = signal(false);

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);

    const { email } = this.form.value;

    this.authService
      .forgotPassword({ email: email.trim().toLowerCase() })
      .subscribe({
        next: () => {
          this.submitted.set(true);
          this.isLoading.set(false);
        },
        error: () => {
          // Always show success to prevent user enumeration (AC-1 of US-AUTH-004)
          this.submitted.set(true);
          this.isLoading.set(false);
        },
      });
  }
}
