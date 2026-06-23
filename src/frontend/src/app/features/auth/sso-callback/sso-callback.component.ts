import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * CR-AUTH-001 (US-AUTH-015): landing route for the Microsoft SSO redirect. The backend callback has
 * already validated the login and set the refresh cookie, then redirected here with the post-login
 * returnUrl. We complete the session client-side (refresh → /auth/me) and navigate on.
 */
@Component({
  selector: 'app-sso-callback',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col items-center justify-center gap-4 py-12 text-center">
      <div
        class="h-10 w-10 animate-spin rounded-full border-4 border-gray-200 border-t-indigo-600"
        role="status"
        aria-label="Signing in"
      ></div>
      <p class="text-sm text-gray-600">Signing you in with Microsoft…</p>
    </div>
  `,
})
export class SsoCallbackComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly failed = signal(false);

  ngOnInit(): void {
    const raw = this.route.snapshot.queryParamMap.get('returnUrl');
    // Only allow internal paths (open-redirect hardening).
    const returnUrl = raw && raw.startsWith('/') ? raw : '/dashboard';

    this.auth.completeSsoLogin().subscribe({
      next: () => this.router.navigateByUrl(returnUrl),
      error: () => {
        this.failed.set(true);
        this.router.navigate(['/auth/login'], {
          queryParams: { sso_error: 'sso_failed' },
        });
      },
    });
  }
}
