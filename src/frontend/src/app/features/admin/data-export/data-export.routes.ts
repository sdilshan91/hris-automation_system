import { Routes } from '@angular/router';

/**
 * US-ADM-010: Tenant-Admin data-export routes.
 * Mounted under `/admin/data-export` inside the authenticated MainLayout and
 * gated by roleGuard(['Tenant Admin', 'Tenant Owner']) in app.routes.ts (BR-1).
 * The System-Admin persona reaches the same export flow via the tenant-detail
 * "Export Data" dialog (AC-6), not through this route.
 */
export const DATA_EXPORT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/export-page/export-page.component').then(
        (m) => m.ExportPageComponent
      ),
  },
];
