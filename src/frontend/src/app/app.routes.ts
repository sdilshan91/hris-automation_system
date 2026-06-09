import { Routes } from '@angular/router';
import { authGuard, noAuthGuard, roleGuard } from './core/auth/auth.guard';
import { mfaChallengeGuard, mfaEnrollGuard } from './core/auth/mfa.guard';
import { tenantAvailabilityGuard } from './core/tenant/tenant.guard';
import { AuthLayoutComponent } from './layouts/auth-layout/auth-layout.component';
import { MainLayoutComponent } from './layouts/main-layout/main-layout.component';

export const appRoutes: Routes = [
  {
    path: 'workspace-not-found',
    loadComponent: () =>
      import('./features/workspace/workspace-not-found.component').then(
        (m) => m.WorkspaceNotFoundComponent
      ),
  },
  {
    path: 'tenant-suspended',
    loadComponent: () =>
      import('./features/workspace/tenant-suspended.component').then(
        (m) => m.TenantSuspendedComponent
      ),
  },

  // ─── Auth routes (no auth required) ──────────────────────
  {
    path: 'auth',
    component: AuthLayoutComponent,
    canActivate: [tenantAvailabilityGuard, noAuthGuard],
    children: [
      {
        path: 'login',
        loadComponent: () =>
          import('./features/auth/login/login.component').then(
            (m) => m.LoginComponent
          ),
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import(
            './features/auth/forgot-password/forgot-password.component'
          ).then((m) => m.ForgotPasswordComponent),
      },
      {
        path: 'reset-password',
        loadComponent: () =>
          import(
            './features/auth/reset-password/reset-password.component'
          ).then((m) => m.ResetPasswordComponent),
      },
      { path: '', redirectTo: 'login', pathMatch: 'full' },
    ],
  },

  // ─── MFA routes (mid-login flow — no auth/noAuth guard, custom guards) ─
  {
    path: 'auth/mfa',
    component: AuthLayoutComponent,
    canActivate: [tenantAvailabilityGuard],
    children: [
      {
        path: 'challenge',
        canActivate: [mfaChallengeGuard],
        loadComponent: () =>
          import(
            './features/auth/mfa/mfa-challenge/mfa-challenge.component'
          ).then((m) => m.MfaChallengeComponent),
      },
      {
        path: 'enroll',
        canActivate: [mfaEnrollGuard],
        loadComponent: () =>
          import(
            './features/auth/mfa/mfa-enroll/mfa-enroll.component'
          ).then((m) => m.MfaEnrollComponent),
      },
    ],
  },

  // ─── Authenticated routes ────────────────────────────────
  {
    path: '',
    component: MainLayoutComponent,
    canActivate: [tenantAvailabilityGuard, authGuard],
    children: [
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(
            (m) => m.DashboardComponent
          ),
      },
      // ─── Admin / Roles (US-AUTH-006) ──────────────────────
      {
        path: 'admin/roles',
        loadChildren: () =>
          import('./features/admin/roles/roles.routes').then(
            (m) => m.ROLES_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'Tenant Owner']),
        ],
      },
      // MFA settings page (user profile security)
      {
        path: 'auth/mfa/settings',
        loadComponent: () =>
          import(
            './features/auth/mfa/mfa-settings/mfa-settings.component'
          ).then((m) => m.MfaSettingsComponent),
      },
      // Tenant admin auth settings
      {
        path: 'admin/tenant/auth-settings',
        canActivate: [roleGuard(['Tenant Admin'])],
        loadComponent: () =>
          import(
            './features/auth/mfa/tenant-auth-settings/tenant-auth-settings.component'
          ).then((m) => m.TenantAuthSettingsComponent),
      },
      // Feature modules will be added here as they are implemented
      // {
      //   path: 'employees',
      //   loadChildren: () => import('./features/employees/employees.routes')
      //     .then(m => m.EMPLOYEE_ROUTES),
      //   canActivate: [permissionGuard(['Employee.View.All', 'Employee.View.Team'])],
      // },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ],
  },

  // ─── Forbidden page ──────────────────────────────────────
  {
    path: 'forbidden',
    loadComponent: () =>
      import('./features/auth/forbidden/forbidden.component').then(
        (m) => m.ForbiddenComponent
      ),
  },

  // ─── Wildcard ────────────────────────────────────────────
  {
    path: '**',
    redirectTo: 'auth/login',
  },
];
