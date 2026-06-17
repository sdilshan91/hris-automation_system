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

  // ─── Public careers (US-REC-002) — anonymous, NO auth/role guard ──
  // Tenant resolved from subdomain; deliberately outside MainLayout/authGuard
  // so external applicants can browse open vacancies and apply (NFR-2).
  {
    path: 'careers',
    canActivate: [tenantAvailabilityGuard],
    loadChildren: () =>
      import('./features/recruitment/careers.routes').then(
        (m) => m.CAREERS_ROUTES
      ),
  },

  // ─── Candidate portal (US-REC-008) — anonymous magic link, NO guard ──
  // Access is controlled by the `?token=` magic-link query param (resolved by the
  // backend), not a session. Tenant is resolved from the subdomain + carried by
  // the tenantInterceptor, mirroring the public careers wiring above.
  {
    path: 'portal',
    canActivate: [tenantAvailabilityGuard],
    loadChildren: () =>
      import('./features/recruitment/portal.routes').then(
        (m) => m.PORTAL_ROUTES
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
      // ─── Admin / Tenants (US-ADM-001) — System Admin Console ──
      // Platform/system context (admin.yourhrm.com). Only the SystemAdmin
      // role may provision tenants (BR-1); SystemSupport is denied.
      {
        path: 'admin/tenants',
        loadChildren: () =>
          import('./features/admin/tenants/tenants.routes').then(
            (m) => m.TENANT_ROUTES
          ),
        canActivate: [roleGuard(['System Admin'])],
      },
      // ─── Admin / Monitoring (US-ADM-002) — System Admin Console ──
      // Platform/system context (admin.yourhrm.com). Both System Admin and
      // System Support may view the monitoring dashboard (BR-1); System Support
      // is read-only — privileged quick-actions are hidden in-component.
      {
        path: 'admin/monitoring',
        loadChildren: () =>
          import('./features/admin/monitoring/monitoring.routes').then(
            (m) => m.MONITORING_ROUTES
          ),
        canActivate: [roleGuard(['System Admin', 'System Support'])],
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
      // ─── Admin / Users (US-ADM-005) — Tenant Admin user & role mgmt ──
      // Tenant context (NOT the system-admin console). Lists tenant memberships,
      // invites users, edits roles, deactivate/force-reset/end-sessions.
      // NOTE: registered AFTER the more-specific 'admin/users/lockout' and
      // 'admin/users/:userId/sessions' routes above so those keep priority over
      // this feature's ':userTenantId' detail child.
      {
        path: 'admin/users',
        loadChildren: () =>
          import(
            './features/admin/user-management/user-management.routes'
          ).then((m) => m.USER_MANAGEMENT_ROUTES),
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
      },
      // ─── Admin / Company Settings (US-ADM-006) — Tenant Admin ──
      // Tenant context (NOT the system-admin console). Org profile, branding,
      // localization, password + session policy. Only Tenant Admin / Tenant
      // Owner may modify company settings (BR-1).
      {
        path: 'admin/settings',
        loadChildren: () =>
          import(
            './features/admin/company-settings/company-settings.routes'
          ).then((m) => m.COMPANY_SETTINGS_ROUTES),
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
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
      // ─── Attendance / Clock-In (US-ATT-001) ─────────────────
      {
        path: 'attendance',
        loadChildren: () =>
          import('./features/attendance/attendance.routes').then(
            (m) => m.ATTENDANCE_ROUTES
          ),
        canActivate: [
          roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin']),
        ],
      },
      // ─── Recruitment / Internal apply (US-REC-002 AC-4) ─────
      // Authenticated employees (broad role set) view an open vacancy and apply
      // internally with a pre-filled slide-over. Distinct from the recruiter-only
      // management screens below.
      {
        path: 'internal-careers/:id',
        loadComponent: () =>
          import(
            './features/recruitment/components/careers/internal-vacancy/internal-vacancy.component'
          ).then((m) => m.InternalVacancyComponent),
        canActivate: [
          roleGuard([
            'Employee',
            'Manager',
            'Recruiter',
            'HR Officer',
            'HR Manager',
            'Tenant Admin',
          ]),
        ],
      },
      // ─── Recruitment / Vacancies (US-REC-001) ──────────────
      {
        path: 'recruitment',
        loadChildren: () =>
          import('./features/recruitment/recruitment.routes').then(
            (m) => m.RECRUITMENT_ROUTES
          ),
        canActivate: [
          roleGuard(['Recruiter', 'HR Officer', 'HR Manager', 'Tenant Admin']),
        ],
      },
      // ─── Payroll / Salary structures + components (US-PAY-001) ─
      {
        path: 'payroll',
        loadChildren: () =>
          import('./features/payroll/payroll.routes').then(
            (m) => m.PAYROLL_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer']),
        ],
      },
      // ─── Performance / Goal-setting (US-PRF-001) ──────────────
      {
        path: 'performance',
        loadChildren: () =>
          import('./features/performance/performance.routes').then(
            (m) => m.PERFORMANCE_ROUTES
          ),
        canActivate: [
          roleGuard(['Manager', 'HR Officer', 'HR Manager', 'Tenant Admin']),
        ],
      },
      // ─── Performance / My Review (US-PRF-002) — employee self-service ─
      // Employee self-assessment against assigned goals. Distinct from the
      // manager/HR-facing '/performance' screens above; the backend enforces
      // Performance.Read.Self so an employee only ever sees their OWN review.
      {
        path: 'my-review',
        loadChildren: () =>
          import('./features/performance/my-review.routes').then(
            (m) => m.MY_REVIEW_ROUTES
          ),
        canActivate: [
          roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin']),
        ],
      },
      // ─── Payroll / My Payslips (US-PAY-005) — employee self-service ─
      // Distinct from the HR-facing '/payroll' screens above (Tenant Admin / HR
      // Officer). Any authenticated employee can view their OWN payslips; the
      // backend enforces Payroll.Read.Self so cross-employee/tenant data is invisible.
      {
        path: 'my-payslips',
        loadChildren: () =>
          import('./features/payroll/my-payslips.routes').then(
            (m) => m.MY_PAYSLIPS_ROUTES
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
