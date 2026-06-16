import { Routes } from '@angular/router';

/**
 * US-ADM-001: System Admin Console — tenant provisioning routes (lazy-loaded).
 * These live in the platform/system-admin context (admin.yourhrm.com), gated by
 * the SystemAdmin role at the app-routes level.
 */
export const TENANT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/tenant-list/tenant-list.component').then(
        (m) => m.TenantListComponent,
      ),
  },
  {
    path: 'create',
    loadComponent: () =>
      import('./components/tenant-create/tenant-create.component').then(
        (m) => m.TenantCreateComponent,
      ),
  },
];
