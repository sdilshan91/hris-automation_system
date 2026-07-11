import { Routes } from '@angular/router';

/**
 * US-TRN-002: Benefit-plan administration routes.
 *
 * Lazy-loaded under the 'benefits' path in app.routes.ts. The parent route
 * applies permissionGuard(['Benefits.View.Own','Benefits.View.All','Benefits.Manage'])
 * so read-only users (View.*) can reach the list while writes stay Manage-only.
 */
export const BENEFITS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/plan-list/plan-list.component').then(
        (m) => m.PlanListComponent
      ),
  },
];
