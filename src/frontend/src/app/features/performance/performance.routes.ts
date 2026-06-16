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
    // US-PRF-006 AC-1/AC-2/AC-4: manager meeting-notes + request employee sign-off.
    // STATIC 'signoff' segment under reviews/:employeeId; the active reviewId flows
    // in as a ?reviewId query param. Declared BEFORE 'reviews/:employeeId' so the
    // static segment is matched first.
    path: 'reviews/:employeeId/signoff',
    loadComponent: () =>
      import('./components/review-signoff/review-signoff.component').then(
        (m) => m.ReviewSignoffComponent,
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
  {
    // US-PRF-004 FR-7/§8: HR appraisal-cycle list (the cycle-management entry point).
    path: 'cycles',
    loadComponent: () =>
      import('./components/cycle-list/cycle-list.component').then(
        (m) => m.CycleListComponent,
      ),
  },
  {
    // US-PRF-004 AC-1: create a new cycle.
    path: 'cycles/new',
    loadComponent: () =>
      import('./components/cycle-form/cycle-form.component').then(
        (m) => m.CycleFormComponent,
      ),
  },
  {
    // US-PRF-004 AC-5: edit a cycle / extend a phase deadline.
    path: 'cycles/:cycleId/edit',
    loadComponent: () =>
      import('./components/cycle-form/cycle-form.component').then(
        (m) => m.CycleFormComponent,
      ),
  },
  {
    // US-PRF-004 AC-3/FR-7/FR-8: cycle dashboard (timeline, stats, transitions, clone).
    path: 'cycles/:cycleId',
    loadComponent: () =>
      import('./components/cycle-dashboard/cycle-dashboard.component').then(
        (m) => m.CycleDashboardComponent,
      ),
  },
  {
    // US-PRF-005 AC-2: a reviewer's 360 feedback form (deep link). Static segment
    // 'assignment' keeps it from colliding with the :employeeId config route below.
    path: 'feedback-360/assignment/:assignmentId',
    loadComponent: () =>
      import(
        './components/feedback-360-form/feedback-360-form.component'
      ).then((m) => m.Feedback360FormComponent),
  },
  {
    // US-PRF-005 AC-4: aggregated 360 results dashboard for an employee.
    path: 'feedback-360/:employeeId/results',
    loadComponent: () =>
      import(
        './components/feedback-360-results/feedback-360-results.component'
      ).then((m) => m.Feedback360ResultsComponent),
  },
  {
    // US-PRF-005 AC-1/AC-3: 360 reviewer nomination + completion tracker.
    path: 'feedback-360/:employeeId',
    loadComponent: () =>
      import(
        './components/feedback-360-config/feedback-360-config.component'
      ).then((m) => m.Feedback360ConfigComponent),
  },
  {
    // US-PRF-008 AC-1: PIP creation form. STATIC 'new' segment declared BEFORE
    // 'pips/:pipId' so it matches first.
    path: 'pips/new',
    loadComponent: () =>
      import('./components/pip-form/pip-form.component').then(
        (m) => m.PipFormComponent,
      ),
  },
  {
    // US-PRF-008 AC-3/AC-4/AC-5/§8: PIP detail/timeline (record checkpoint, HR-only
    // outcome + escalation). Manager/HR role-gated by the parent /performance guard;
    // the backend restricts visibility (FR-8) and HR-only actions (BR-1).
    path: 'pips/:pipId',
    loadComponent: () =>
      import('./components/pip-detail/pip-detail.component').then(
        (m) => m.PipDetailComponent,
      ),
  },
  {
    // US-PRF-008 AC-1: PIP list — the Performance > PIP entry point.
    path: 'pips',
    loadComponent: () =>
      import('./components/pip-list/pip-list.component').then(
        (m) => m.PipListComponent,
      ),
  },
  {
    // US-PRF-007 FR-5: department drill-down (Dashboard > Department > Employee).
    // Declared BEFORE the 'dashboard' overview route so the more-specific
    // static 'departments' segment matches first.
    path: 'dashboard/departments/:departmentId',
    loadComponent: () =>
      import(
        './components/department-drilldown/department-drilldown.component'
      ).then((m) => m.DepartmentDrilldownComponent),
  },
  {
    // US-PRF-007 AC-1..AC-5: the Performance Dashboard & Analytics overview
    // (HR/manager-gated; scope decided server-side, employees redirected to /my-review).
    path: 'dashboard',
    loadComponent: () =>
      import(
        './components/performance-dashboard/performance-dashboard.component'
      ).then((m) => m.PerformanceDashboardComponent),
  },
];
