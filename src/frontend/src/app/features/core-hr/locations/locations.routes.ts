import { Routes } from '@angular/router';

/**
 * US-CHR-007: Location management routes.
 *
 * Lazy-loaded under the 'locations' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const LOCATION_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/location-list/location-list.component').then(
        (m) => m.LocationListComponent
      ),
  },
];
