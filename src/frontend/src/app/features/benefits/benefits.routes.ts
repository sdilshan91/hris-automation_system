import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/auth.guard';

/**
 * US-TRN-002 / US-TRN-003: Benefit routes.
 *
 * Lazy-loaded under the 'benefits' path in app.routes.ts. The parent route
 * applies permissionGuard(['Benefits.View.Own','Benefits.View.All','Benefits.Manage'])
 * so read-only users (View.*) can reach the list while writes stay Manage-only.
 *
 * The 'my-benefits' self-service child (US-TRN-003) additionally gates on
 * Benefits.View.Own so its access exactly matches the "My Benefits" nav entry
 * (ISSUE-210: nav visibility == route access).
 */
export const BENEFITS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/plan-list/plan-list.component').then(
        (m) => m.PlanListComponent
      ),
  },
  {
    path: 'my-benefits',
    loadComponent: () =>
      import('./components/my-benefits/my-benefits.component').then(
        (m) => m.MyBenefitsComponent
      ),
    canActivate: [permissionGuard(['Benefits.View.Own'])],
  },
];
