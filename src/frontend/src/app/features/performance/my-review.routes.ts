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
  {
    // US-PRF-006 AC-3/AC-4/FR-4: employee reviews meeting notes then acknowledges &
    // signs or disputes. Same employee self-service guard as the parent /my-review.
    path: 'sign-off/:reviewId',
    loadComponent: () =>
      import(
        './components/review-signoff-employee/review-signoff-employee.component'
      ).then((m) => m.ReviewSignoffEmployeeComponent),
  },
  {
    // US-PRF-009 AC-1/AC-2/AC-3: employee "My Goals" — goal cards with progress %,
    // animated bars, status chips, the Add-Update bottom-sheet, and the per-goal
    // append-only update timeline. Same employee self-service guard as /my-review;
    // the backend enforces Performance.Read.Self + RLS (own goals only).
    path: 'my-goals',
    loadComponent: () =>
      import('./components/my-goals/my-goals.component').then(
        (m) => m.MyGoalsComponent,
      ),
  },
  {
    // US-PRF-008 BR-4/FR-8: the employee's own PIP ("My PIP") — read-only view +
    // acknowledge initiation. Same employee self-service guard as /my-review; the
    // backend enforces PIP ownership + RLS so an employee sees only their OWN PIP.
    path: 'pip/:pipId',
    loadComponent: () =>
      import('./components/my-pip/my-pip.component').then(
        (m) => m.MyPipComponent,
      ),
  },
];
