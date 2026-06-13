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
      // Session policy settings (US-AUTH-009 FR-1)
      {
        path: 'admin/tenant/session-policy',
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
        loadComponent: () =>
          import(
            './features/auth/sessions/session-policy-settings/session-policy-settings.component'
          ).then((m) => m.SessionPolicySettingsComponent),
      },
      // My active sessions (US-AUTH-009 AC-6)
      {
        path: 'auth/sessions',
        loadComponent: () =>
          import(
            './features/auth/sessions/my-sessions/my-sessions.component'
          ).then((m) => m.MySessionsComponent),
      },
      // Admin view of user sessions (US-AUTH-009 AC-4/AC-5)
      {
        path: 'admin/users/:userId/sessions',
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
        loadComponent: () =>
          import(
            './features/auth/sessions/admin-user-sessions/admin-user-sessions.component'
          ).then((m) => m.AdminUserSessionsComponent),
      },
      // Lockout policy settings (US-AUTH-010 FR-3)
      {
        path: 'admin/tenant/lockout-policy',
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
        loadComponent: () =>
          import(
            './features/auth/lockout/lockout-policy-settings/lockout-policy-settings.component'
          ).then((m) => m.LockoutPolicySettingsComponent),
      },
      // Admin user lockout management (US-AUTH-010 AC-5 / FR-6)
      {
        path: 'admin/users/lockout',
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
        loadComponent: () =>
          import(
            './features/auth/lockout/admin-user-lockout/admin-user-lockout.component'
          ).then((m) => m.AdminUserLockoutComponent),
      },
      // ─── Core HR / Departments (US-CHR-004) ───────────────
      {
        path: 'departments',
        loadChildren: () =>
          import('./features/core-hr/departments/departments.routes').then(
            (m) => m.DEPARTMENT_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Core HR / Job Titles (US-CHR-005) ─────────────────
      {
        path: 'job-titles',
        loadChildren: () =>
          import('./features/core-hr/job-titles/job-titles.routes').then(
            (m) => m.JOB_TITLE_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Core HR / Locations (US-CHR-007) ───────────────────
      {
        path: 'locations',
        loadChildren: () =>
          import('./features/core-hr/locations/locations.routes').then(
            (m) => m.LOCATION_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Core HR / Custom Fields (US-CHR-012) ────────────────
      {
        path: 'settings/custom-fields',
        loadChildren: () =>
          import('./features/core-hr/custom-fields/custom-fields.routes').then(
            (m) => m.CUSTOM_FIELD_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin']),
        ],
      },
      // ─── Leave Management / Leave Types (US-LV-001) ─────────
      {
        path: 'leave-types',
        loadChildren: () =>
          import('./features/leave-management/leave-management.routes').then(
            (m) => m.LEAVE_MANAGEMENT_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Leave Management / Apply + My Requests (US-LV-003) ──
      {
        path: 'leave',
        loadChildren: () =>
          import('./features/leave-management/leave-management.routes').then(
            (m) => m.LEAVE_REQUEST_ROUTES
          ),
        canActivate: [
          roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin']),
        ],
      },
      // ─── Core HR / Employees (US-CHR-001) ──────────────────
      {
        path: 'employees',
        loadChildren: () =>
          import('./features/core-hr/employees/employees.routes').then(
            (m) => m.EMPLOYEE_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Core HR / Organization Tree (US-CHR-006) ──────────
      {
        path: 'org-tree',
        loadChildren: () =>
          import('./features/core-hr/org-tree/org-tree.routes').then(
            (m) => m.ORG_TREE_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer', 'Manager']),
        ],
      },
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
