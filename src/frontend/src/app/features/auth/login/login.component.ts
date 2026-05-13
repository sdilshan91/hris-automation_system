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

    const { email, password } = this.loginForm.value;

    this.authService
      .login({ email: email.trim().toLowerCase(), password })
      .subscribe({
        next: (response) => {
          if (!response.mfaChallenge) {
            this.router.navigate(['/dashboard']);
          }
          // If MFA challenge, the template switches to MFA view via authService.mfaChallenge()
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
      const message =
        err.error?.message || 'Invalid email or password.';
      this.errorMessage.set(message);
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
