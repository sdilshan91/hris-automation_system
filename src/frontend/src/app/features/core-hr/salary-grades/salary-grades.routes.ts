import { Routes } from '@angular/router';

/**
 * ISSUE-021: Salary Grade management routes.
 *
 * Lazy-loaded under the 'salary-grades' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']),
 * mirroring the job-titles admin route.
 */
export const SALARY_GRADE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/salary-grade-list/salary-grade-list.component').then(
        (m) => m.SalaryGradeListComponent
      ),
  },
];
