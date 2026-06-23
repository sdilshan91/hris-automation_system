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
      {
        // CR-AUTH-001: Microsoft SSO redirect landing route. The user is not yet authenticated in the
        // SPA on arrival (noAuthGuard passes); completeSsoLogin() then navigates to the returnUrl.
        path: 'sso/callback',
        loadComponent: () =>
          import('./features/auth/sso-callback/sso-callback.component').then(
            (m) => m.SsoCallbackComponent
          ),
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
        canActivate: [roleGuard(['SystemAdmin'])],
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
        canActivate: [roleGuard(['SystemAdmin', 'System Support'])],
      },
      // ─── Admin / Plans (US-ADM-009) — System Admin Console ──
      // Platform/system context (admin.yourhrm.com). Only the System Admin role
      // may create/edit/archive subscription plans (BR-1); tenant roles cannot
      // view or modify them. Per-tenant limit overrides (AC-5) live on the
      // US-ADM-002 tenant-monitoring detail, not here.
      {
        path: 'admin/plans',
        loadChildren: () =>
          import('./features/admin/plans/plans.routes').then(
            (m) => m.PLANS_ROUTES
          ),
        canActivate: [roleGuard(['SystemAdmin'])],
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
      // ─── Admin / Approval Workflows (US-ADM-007) — Tenant Admin ──
      // Tenant context (NOT the system-admin console). Configures approval
      // workflow DEFINITIONS (steps / approvers / SLA / conditions / escalation
      // / delegation) per request type. Runtime engine is backend-deferred.
      // Only Tenant Admin / Tenant Owner may manage workflows (BR-1).
      {
        path: 'admin/workflows',
        loadChildren: () =>
          import('./features/admin/workflows/workflows.routes').then(
            (m) => m.WORKFLOWS_ROUTES
          ),
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
      },
      // ─── Admin / Audit Log (US-ADM-008) — Tenant Admin ──
      // Tenant context (NOT the system-admin console; that is system_audit_log,
      // BR-3). Read-only viewer of tenant-scoped audit records with filters,
      // visual JSON diff, and filtered CSV/JSON export. Tenant Admin / Tenant
      // Owner / Auditor may view (BR-1); the Auditor role is read-only (FR-7).
      {
        path: 'admin/audit-log',
        loadChildren: () =>
          import('./features/admin/audit-log/audit-log.routes').then(
            (m) => m.AUDIT_LOG_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'Tenant Owner', 'Auditor']),
        ],
      },
      // ─── Admin / Data Export (US-ADM-010) — Tenant Admin ──
      // Tenant context (Data Management > Export). On-demand full/partial data
      // export (GDPR portability) with format prefs, a history list, and a
      // progress indicator. Tenant Admin / Tenant Owner only (BR-1). The
      // System-Admin persona reaches the same flow via the tenant-detail
      // "Export Data" dialog (AC-6), targeting the system path.
      {
        path: 'admin/data-export',
        loadChildren: () =>
          import('./features/admin/data-export/data-export.routes').then(
            (m) => m.DATA_EXPORT_ROUTES
          ),
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
      },
      // ─── Reports & Analytics / Pre-Built HR Reports (US-RPT-001) ──
      // Tenant context. HR Officers (Reports.View) browse a catalog of six
      // pre-built reports (headcount / turnover / demographics / joiners-
      // leavers / department-distribution / employment-type) and render any of
      // them through a single generic viewer with charts + a data-table
      // alternative. Tenant isolation is enforced server-side (AC-5). The
      // feature also self-guards each route with permissionGuard(['Reports.View']).
      {
        path: 'reports',
        loadChildren: () =>
          import('./features/reports/reports.routes').then(
            (m) => m.REPORTS_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager', 'Manager']),
        ],
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
      // ─── Onboarding: new-hire self-service (US-ONB-003) ────
      // The employee's own checklist must be reachable by a plain Employee, so it
      // sits ABOVE the HR-only 'onboarding' block and carries NO HR roleGuard
      // (authGuard from the parent still applies; the backend scopes to the
      // caller's identity + tenant). FR-1.
      {
        path: 'onboarding/my-checklist',
        loadComponent: () =>
          import(
            './features/onboarding/components/my-checklist/my-checklist.component'
          ).then((m) => m.MyChecklistComponent),
      },
      // US-ONB-004: the employee's own assigned assets — READ-ONLY self-service
      // (AC-4 / BR-6). Same rationale as my-checklist: reachable by a plain
      // Employee, so it sits ABOVE the HR-only 'onboarding' block with NO HR
      // roleGuard; the backend scopes to the caller's identity + tenant.
      {
        path: 'onboarding/my-assets',
        loadComponent: () =>
          import(
            './features/onboarding/components/my-assets/my-assets.component'
          ).then((m) => m.MyAssetsComponent),
      },
      // ─── Offboarding / Exit Clearance (US-ONB-005) ─────────
      // Tenant context. HR initiates offboarding (offboarding/initiate/:employeeId)
      // and works the clearance dashboard (offboarding/:offboardingId). Sits as its
      // own top-level path so the :offboardingId leaf does not collide with the
      // onboarding path space below. Department-head clearance actions are
      // authorized server-side per task; the route guard scopes by HR role.
      {
        path: 'offboarding',
        loadChildren: () =>
          import('./features/onboarding/offboarding.routes').then(
            (m) => m.OFFBOARDING_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager']),
        ],
      },
      // ─── Exit Interview / Analytics (US-ONB-006) — HR-only ──
      // The Exit Interview Summary (reason pie / category bars / trend line) shows
      // tenant-scoped AGGREGATES only (FR-4/FR-5). HR-gated. Registered ABOVE the
      // self-service form route so the static 'analytics' segment wins over the
      // form's ':offboardingId' leaf.
      {
        path: 'exit-interview/analytics',
        loadComponent: () =>
          import(
            './features/onboarding/components/exit-interview-analytics/exit-interview-analytics.component'
          ).then((m) => m.ExitInterviewAnalyticsComponent),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager']),
        ],
      },
      // ─── Exit Interview / Questionnaire (US-ONB-006) ────────
      // The structured questionnaire (AC-1/AC-2/AC-3). Reachable by BOTH HR
      // (hr_conducted) and the departing employee (self_service); the mode is
      // DERIVED from role in-component and the backend re-derives + authorizes
      // (FR-2/FR-6). Like my-checklist/my-assets it carries NO HR roleGuard so a
      // plain Employee can complete their own exit interview — the parent authGuard
      // still applies and the backend scopes to the caller's identity + tenant.
      {
        path: 'exit-interview/:offboardingId',
        loadComponent: () =>
          import(
            './features/onboarding/components/exit-interview-form/exit-interview-form.component'
          ).then((m) => m.ExitInterviewFormComponent),
      },
      // ─── Onboarding / Checklist Templates (US-ONB-001) ─────
      {
        path: 'onboarding',
        loadChildren: () =>
          import('./features/onboarding/onboarding.routes').then(
            (m) => m.ONBOARDING_ROUTES
          ),
        canActivate: [
          roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager']),
        ],
      },
      // ─── Admin / Notification Templates (US-NTF-002) — Tenant Admin ──
      // Tenant context (Settings > Notifications & Email > Templates). Tenant
      // Admins customise per-event email templates (subject / HTML / plain-text)
      // with a live preview; overrides are tenant-scoped server-side (AC-3/AC-5).
      // Only Tenant Admin / Tenant Owner may edit templates (BR-1), matching the
      // sibling US-ADM-006 company-settings gate.
      {
        path: 'admin/notification-templates',
        loadChildren: () =>
          import(
            './features/notifications/notification-templates.routes'
          ).then((m) => m.NOTIFICATION_TEMPLATES_ROUTES),
        canActivate: [roleGuard(['Tenant Admin', 'Tenant Owner'])],
      },
      // ─── Profile / Notification Preferences (US-NTF-003) ──────
      // Personal settings under the Profile area. No extra roleGuard: any
      // authenticated user manages their OWN per-tenant-membership preferences
      // (the parent authGuard is sufficient; the backend scopes to identity +
      // tenant membership, BR-4). Registered as its own profile sub-path.
      {
        path: 'profile/notification-preferences',
        loadChildren: () =>
          import(
            './features/notifications/notification-preferences.routes'
          ).then((m) => m.NOTIFICATION_PREFERENCES_ROUTES),
      },
      // ─── Notifications (US-NTF-001) — all authenticated users ──
      // The bell's "View All" link lands here. No extra roleGuard: notifications
      // are personal to the signed-in user (the backend scopes to identity +
      // tenant); the parent authGuard is sufficient.
      {
        path: 'notifications',
        loadChildren: () =>
          import('./features/notifications/notifications.routes').then(
            (m) => m.NOTIFICATIONS_ROUTES
          ),
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
