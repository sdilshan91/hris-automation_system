import { Routes } from '@angular/router';
import { authGuard, noAuthGuard, roleGuard } from './core/auth/auth.guard';
import { AuthLayoutComponent } from './layouts/auth-layout/auth-layout.component';
import { MainLayoutComponent } from './layouts/main-layout/main-layout.component';

export const appRoutes: Routes = [
  // ─── Auth routes (no auth required) ──────────────────────
  {
    path: 'auth',
    component: AuthLayoutComponent,
    canActivate: [noAuthGuard],
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

  // ─── Authenticated routes ────────────────────────────────
  {
    path: '',
    component: MainLayoutComponent,
    canActivate: [authGuard],
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
