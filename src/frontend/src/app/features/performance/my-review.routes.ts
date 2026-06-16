import { Routes } from '@angular/router';

/**
 * US-PRF-002: Employee self-assessment ("My Review") route.
 *
 * Lazy-loaded under the 'my-review' path in app.routes.ts. This is the EMPLOYEE
 * persona view (AC-1) — distinct from the manager/HR-facing '/performance'
 * goal-setting screens (US-PRF-001) which are guarded by
 * ['Manager','HR Officer','HR Manager','Tenant Admin']. The parent '/my-review'
 * route uses a permissive role guard so any authenticated employee can reach their
 * OWN review; the backend enforces `Performance.Read.Self` + RLS so cross-employee /
 * cross-tenant data is invisible (NFR-2). Mirrors the '/my-payslips' self-service
 * pattern (US-PAY-005).
 */
export const MY_REVIEW_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/my-review/my-review.component').then(
        (m) => m.MyReviewComponent,
      ),
  },
];
