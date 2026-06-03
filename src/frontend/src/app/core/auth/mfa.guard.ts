import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Guard that only allows access when an MFA challenge is in progress.
 * Redirects to /auth/login if no challenge is active.
 */
export const mfaChallengeGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.mfaChallenge()) {
    return true;
  }

  return router.createUrlTree(['/auth/login']);
};

/**
 * Guard that only allows access when MFA enrollment is required.
 * Redirects to /auth/login if no enrollment is pending.
 */
export const mfaEnrollGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.mfaChallenge() && authService.mfaRequiresEnrollment()) {
    return true;
  }

  // Also allow authenticated users to access enrollment voluntarily
  if (authService.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/auth/login']);
};
