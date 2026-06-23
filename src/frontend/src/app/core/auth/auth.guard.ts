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
/**
 * Built-in roles that are authorized for EVERY tenant-scoped route by definition: the backend seeds
 * "Tenant Owner" with all tenant permissions (PermissionCatalog.DefaultPermissionsFor), so it must never be
 * locked out of a tenant feature. Implicitly allowing it here means new routes can't accidentally omit it
 * from their role list (a recurring bug). It is NOT applied to platform/system guards (see below), so a
 * Tenant Owner still cannot reach the system-admin console.
 */
const TENANT_SUPER_ROLES = ['Tenant Owner'];
const SYSTEM_ROLES = ['SystemAdmin', 'System Support'];

export function roleGuard(requiredRoles: string[]): CanActivateFn {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    // Only widen tenant guards. A guard that lists a system role is a platform/system-console guard and
    // must stay exactly as specified (a Tenant Owner is not a system admin).
    const isSystemGuard = requiredRoles.some((role) => SYSTEM_ROLES.includes(role));
    const effectiveRoles = isSystemGuard
      ? requiredRoles
      : [...requiredRoles, ...TENANT_SUPER_ROLES];

    const hasRole = effectiveRoles.some((role) => authService.hasRole(role));

    if (hasRole) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };
}
