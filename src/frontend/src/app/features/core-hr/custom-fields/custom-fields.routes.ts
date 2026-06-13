import { Routes } from '@angular/router';

/**
 * US-CHR-012: Custom Fields management routes.
 *
 * Lazy-loaded under the 'settings/custom-fields' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin']).
 */
export const CUSTOM_FIELD_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/custom-field-list/custom-field-list.component').then(
        (m) => m.CustomFieldListComponent
      ),
  },
];
