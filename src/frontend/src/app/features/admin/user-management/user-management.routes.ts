import { Routes } from '@angular/router';

/**
 * US-ADM-005: Tenant Admin user management routes.
 * Mounted under `/admin/users` inside the authenticated MainLayout and gated by
 * roleGuard(['Tenant Admin', 'Tenant Owner']) in app.routes.ts.
 */
export const USER_MANAGEMENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/user-list/user-list.component').then(
        (m) => m.UserListComponent
      ),
  },
  {
    path: ':userTenantId',
    loadComponent: () =>
      import('./components/user-detail/user-detail.component').then(
        (m) => m.UserDetailComponent
      ),
  },
];
