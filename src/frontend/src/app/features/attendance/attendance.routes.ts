import { Routes } from '@angular/router';

/**
 * US-ATT-001: Employee-facing attendance routes (self clock-in).
 *
 * Lazy-loaded under the 'attendance' path in app.routes.ts. The parent route applies
 * roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin']) — clock-in is a
 * self action available to any authenticated employee with the Attendance.Clock.Self
 * permission (the backend enforces the permission; the route guard scopes by role).
 *
 * This is the first Attendance screen; later stories (clock-out, timesheets, shift
 * management US-ATT-005) add sibling child routes here.
 */
export const ATTENDANCE_ROUTES: Routes = [
  {
    // US-ATT-001: self clock-in landing view.
    path: 'clock-in',
    loadComponent: () =>
      import('./components/clock-in/clock-in.component').then(
        (m) => m.ClockInComponent
      ),
  },
  { path: '', redirectTo: 'clock-in', pathMatch: 'full' },
];
