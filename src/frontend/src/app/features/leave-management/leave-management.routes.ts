import { Routes } from '@angular/router';

/**
 * US-LV-001 / US-LV-002: Leave configuration routes (admin / HR).
 *
 * Lazy-loaded under the 'leave-types' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const LEAVE_MANAGEMENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/leave-type-list/leave-type-list.component').then(
        (m) => m.LeaveTypeListComponent
      ),
  },
  {
    path: 'entitlements',
    loadComponent: () =>
      import('./components/entitlement-rules/entitlement-rules.component').then(
        (m) => m.EntitlementRulesComponent
      ),
  },
];

/**
 * US-LV-003: Employee-facing leave routes (apply + my requests).
 *
 * Lazy-loaded under the 'leave' path in app.routes.ts.
 * The parent route applies roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin']).
 */
export const LEAVE_REQUEST_ROUTES: Routes = [
  {
    path: 'apply',
    loadComponent: () =>
      import('./components/leave-application/leave-application.component').then(
        (m) => m.LeaveApplicationComponent
      ),
  },
  {
    path: 'my-requests',
    loadComponent: () =>
      import('./components/my-leave-requests/my-leave-requests.component').then(
        (m) => m.MyLeaveRequestsComponent
      ),
  },
  { path: '', redirectTo: 'my-requests', pathMatch: 'full' },
];
