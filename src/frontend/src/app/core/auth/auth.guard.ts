import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Guard that protects routes requiring authentication.
 * Redirects unauthenticated users to the login page.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/auth/login']);
};

/**
 * Guard that prevents authenticated users from accessing auth pages (login, etc.).
 * Redirects authenticated users to the dashboard.
 */
export const noAuthGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};

/**
 * Guard that checks for specific permissions.
 * Usage in route config: canActivate: [permissionGuard(['Employee.View.All'])]
 */
export function permissionGuard(requiredPermissions: string[]): CanActivateFn {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (authService.hasAnyPermission(requiredPermissions)) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };
}

/**
 * Guard that checks for specific roles.
 * Usage in route config: canActivate: [roleGuard(['Tenant Admin', 'HR Officer'])]
 */
export function roleGuard(requiredRoles: string[]): CanActivateFn {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const hasRole = requiredRoles.some((role) => authService.hasRole(role));

    if (hasRole) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };
}
