import { Routes } from '@angular/router';

/**
 * US-TRN-001: Training catalog routes.
 *
 * Lazy-loaded under the 'training' path in app.routes.ts. The parent route
 * applies permissionGuard(['Training.View.Own','Training.View.All','Training.Manage'])
 * so employees who self-enrol (View.Own) can reach the catalog.
 */
export const TRAINING_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/course-list/course-list.component').then(
        (m) => m.CourseListComponent
      ),
  },
  {
    path: 'my-enrollments',
    loadComponent: () =>
      import('./components/my-enrollments/my-enrollments.component').then(
        (m) => m.MyEnrollmentsComponent
      ),
  },
];
