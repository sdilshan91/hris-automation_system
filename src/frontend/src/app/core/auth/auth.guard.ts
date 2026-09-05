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
 * BUG-493 — GUARD INTROSPECTION.
 *
 * `permissionGuard`/`roleGuard` return a CLOSURE. Once built, the roles/permissions the guard
 * enforces are unrecoverable from the function value, so nothing could mechanically check that a
 * sidebar nav gate agrees with the guard on the route it links to. The two answers to "may this
 * persona use this feature?" therefore drifted silently, in both directions: a link shown to
 * someone the route rejects (dead-ends at /forbidden), or hidden from someone it would admit.
 *
 * The fix is to publish the guard's own criteria ON the returned function. The critical detail is
 * that each guard BODY closes over the very same frozen array that is published — the property is
 * not a parallel description that can drift from the check, it IS the list the check uses. A second
 * hand-maintained copy of route→roles (in a test or anywhere else) would be the same defect class
 * this exists to prevent.
 *
 * Consumed by main-layout.nav-gate-invariant.spec.ts. Runtime behaviour is unchanged.
 */
export type PermissionGuardFn = CanActivateFn & {
  readonly requiredPermissions: readonly string[];
};

export type RoleGuardFn = CanActivateFn & {
  /** Exactly the roles passed at the call site. */
  readonly requiredRoles: readonly string[];
  /** What the guard actually admits — `requiredRoles` after TENANT_SUPER_ROLES widening. */
  readonly effectiveRoles: readonly string[];
  /** True when this is a platform/system guard, which is NOT widened with tenant super roles. */
  readonly isSystemGuard: boolean;
};

export function isPermissionGuard(fn: unknown): fn is PermissionGuardFn {
  return (
    typeof fn === 'function' && Array.isArray((fn as PermissionGuardFn).requiredPermissions)
  );
}

export function isRoleGuard(fn: unknown): fn is RoleGuardFn {
  return typeof fn === 'function' && Array.isArray((fn as RoleGuardFn).effectiveRoles);
}

/**
 * Guard that checks for specific permissions.
 * Usage in route config: canActivate: [permissionGuard(['Employee.View.All'])]
 */
export function permissionGuard(requiredPermissions: string[]): PermissionGuardFn {
  const permissions: readonly string[] = Object.freeze([...requiredPermissions]);

  const guard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (authService.hasAnyPermission([...permissions])) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };

  return Object.assign(guard, { requiredPermissions: permissions });
}

/**
 * Built-in roles that are authorized for EVERY tenant-scoped route by definition: the backend seeds
 * "Tenant Owner" with all tenant permissions (PermissionCatalog.DefaultPermissionsFor), so it must never be
 * locked out of a tenant feature. Implicitly allowing it here means new routes can't accidentally omit it
 * from their role list (a recurring bug). It is NOT applied to platform/system guards (see below), so a
 * Tenant Owner still cannot reach the system-admin console.
 */
export const TENANT_SUPER_ROLES: readonly string[] = Object.freeze(['Tenant Owner']);

/**
 * Platform/system-console roles. Exported because the sidebar's system-vs-tenant persona split has
 * to use the SAME definition the guards use — deriving "is this a platform operator?" separately in
 * the layout is how the System Support persona ended up seeing a tenant menu it cannot enter while
 * its one reachable page (/admin/monitoring) was hidden (BUG-493).
 */
export const SYSTEM_ROLES: readonly string[] = Object.freeze(['SystemAdmin', 'System Support']);

/**
 * Guard that checks for specific roles.
 * Usage in route config: canActivate: [roleGuard(['Tenant Admin', 'HR Officer'])]
 */
export function roleGuard(requiredRoles: string[]): RoleGuardFn {
  // Only widen tenant guards. A guard that lists a system role is a platform/system-console guard and
  // must stay exactly as specified (a Tenant Owner is not a system admin).
  const isSystemGuard = requiredRoles.some((role) => SYSTEM_ROLES.includes(role));
  const effectiveRoles: readonly string[] = Object.freeze(
    isSystemGuard
      ? [...requiredRoles]
      : [...requiredRoles, ...TENANT_SUPER_ROLES.filter((r) => !requiredRoles.includes(r))]
  );

  const guard: CanActivateFn = () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    // Reads the SAME array published below — the introspected list cannot drift from the check.
    const hasRole = effectiveRoles.some((role) => authService.hasRole(role));

    if (hasRole) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };

  return Object.assign(guard, {
    requiredRoles: Object.freeze([...requiredRoles]),
    effectiveRoles,
    isSystemGuard,
  });
}
