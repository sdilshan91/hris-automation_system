import { Routes } from '@angular/router';
import { roleGuard } from '../../core/auth/auth.guard';

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
  {
    // US-ATT-003: attendance regularization (forgot clock-in/out) — list + drawer form.
    path: 'regularization',
    loadComponent: () =>
      import('./components/regularization/regularization.component').then(
        (m) => m.RegularizationComponent
      ),
  },
  {
    // US-ATT-004: Manager's regularization approval queue.
    // The parent 'attendance' route already requires authentication + one of
    // Employee/Manager/HR Officer/Tenant Admin; this child further restricts to
    // approver roles (Manager / HR Officer / Tenant Admin) per the
    // Attendance.Approve.Team capability — same technique the leave-approvals
    // child uses, so NO app.routes.ts edit is needed.
    path: 'regularization-approvals',
    canActivate: [roleGuard(['Manager', 'HR Officer', 'Tenant Admin'])],
    loadComponent: () =>
      import(
        './components/regularization-approvals/regularization-approvals.component'
      ).then((m) => m.RegularizationApprovalsComponent),
  },
  {
    // US-ATT-005: Shift management & assignment. HR-only (HR Officer / HR Manager /
    // Tenant Admin) per §2 (Attendance.*.All) — same child-guard technique as the
    // regularization-approvals route, so NO app.routes.ts edit is needed.
    path: 'shifts',
    canActivate: [roleGuard(['HR Officer', 'HR Manager', 'Tenant Admin'])],
    loadComponent: () =>
      import('./components/shift-management/shift-management.component').then(
        (m) => m.ShiftManagementComponent
      ),
  },
  {
    // US-ATT-006: employee's "My Overtime" list + pre-approval form + weekly-progress
    // bar. A self view — inherits the parent attendance guard (any authenticated
    // employee with Attendance.Clock.Self), so NO extra child guard is needed.
    path: 'overtime',
    loadComponent: () =>
      import('./components/my-overtime/my-overtime.component').then(
        (m) => m.MyOvertimeComponent
      ),
  },
  {
    // US-ATT-006 (AC-3/AC-4): manager's overtime approval queue — the overtime side of
    // the unified approval hub (sibling to regularization-approvals, mirrors its model).
    // Same approver child-guard technique; NO app.routes.ts edit needed.
    path: 'overtime-approvals',
    canActivate: [roleGuard(['Manager', 'HR Officer', 'Tenant Admin'])],
    loadComponent: () =>
      import('./components/overtime-approvals/overtime-approvals.component').then(
        (m) => m.OvertimeApprovalsComponent
      ),
  },
  {
    // US-ATT-006 (AC-5): HR monthly overtime report. HR-only (HR Officer / HR Manager /
    // Tenant Admin) per §2 — same child-guard technique as the shifts route.
    path: 'overtime-report',
    canActivate: [roleGuard(['HR Officer', 'HR Manager', 'Tenant Admin'])],
    loadComponent: () =>
      import('./components/overtime-report/overtime-report.component').then(
        (m) => m.OvertimeReportComponent
      ),
  },
  { path: '', redirectTo: 'clock-in', pathMatch: 'full' },
];
