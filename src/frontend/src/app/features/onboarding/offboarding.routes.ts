import { Routes } from '@angular/router';

/**
 * US-ONB-005: Offboarding / exit-checklist + clearance routes.
 *
 * Lazy-loaded under the 'offboarding' path in app.routes.ts. The parent route
 * applies roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager']) — initiating
 * offboarding and the clearance dashboard are HR-facing (AC-1/AC-3); the backend
 * enforces the actual permission + tenant RLS (AC-6), the route guard scopes by
 * role. Department-head clearance actions are authorized server-side per task.
 *
 * Kept as a SEPARATE route file from ONBOARDING_ROUTES so the offboarding feature
 * has its own top-level 'offboarding/...' paths (the dashboard's :offboardingId
 * leaf would otherwise collide with onboarding's path space).
 */
export const OFFBOARDING_ROUTES: Routes = [
  {
    // HR initiates offboarding for a specific departing employee (routed input()).
    // Optional ?employeeName= pre-fills the heading/toast.
    path: 'initiate/:employeeId',
    loadComponent: () =>
      import(
        './components/offboarding-initiate/offboarding-initiate.component'
      ).then((m) => m.OffboardingInitiateComponent),
  },
  {
    // Clearance dashboard for an offboarding instance (routed input()).
    path: ':offboardingId',
    loadComponent: () =>
      import(
        './components/offboarding-dashboard/offboarding-dashboard.component'
      ).then((m) => m.OffboardingDashboardComponent),
  },
];
