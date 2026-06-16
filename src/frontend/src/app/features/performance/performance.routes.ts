import { Routes } from '@angular/router';

/**
 * US-PRF-001: Performance Management feature routes (first Performance story —
 * establishes the feature folder). Lazy-loaded under the 'performance' path in
 * app.routes.ts; the parent route applies roleGuard for managers/HR. The backend
 * enforces the actual Performance.SetGoal.Team permission (RLS + authz); the route
 * guard scopes by role.
 *
 * Later Performance stories add sibling child routes here (appraisal cycles,
 * reviews, ratings, etc.).
 */
export const PERFORMANCE_ROUTES: Routes = [
  {
    // AC-4: team goals dashboard (the default Performance screen).
    path: '',
    loadComponent: () =>
      import('./components/team-goals/team-goals.component').then(
        (m) => m.TeamGoalsComponent,
      ),
  },
  {
    // AC-1/AC-2/AC-3/AC-5: goal-setting form for one team member.
    path: 'goals/:employeeId',
    loadComponent: () =>
      import('./components/goal-setting/goal-setting.component').then(
        (m) => m.GoalSettingComponent,
      ),
  },
  {
    // US-PRF-003 AC-4: Team Reviews dashboard (manager rates direct reports).
    path: 'team-reviews',
    loadComponent: () =>
      import('./components/team-reviews/team-reviews.component').then(
        (m) => m.TeamReviewsComponent,
      ),
  },
  {
    // US-PRF-003 AC-1/AC-2/AC-3/AC-5: per-employee manager review.
    path: 'reviews/:employeeId',
    loadComponent: () =>
      import('./components/manager-review/manager-review.component').then(
        (m) => m.ManagerReviewComponent,
      ),
  },
];
