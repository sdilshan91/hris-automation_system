import { Routes } from '@angular/router';

/**
 * US-ADM-006: Tenant Admin company-settings routes.
 * Mounted under `/admin/settings` inside the authenticated MainLayout and gated
 * by roleGuard(['Tenant Admin', 'Tenant Owner']) in app.routes.ts.
 */
export const COMPANY_SETTINGS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/company-settings/company-settings.component').then(
        (m) => m.CompanySettingsComponent
      ),
  },
];
