import { Routes } from '@angular/router';
import { roleGuard } from '../../core/auth/auth.guard';

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
  {
    // US-LV-007: Holiday Calendar management (Calendar + List dual view).
    // Registered under 'leave-types' so it shares the same role guard
    // (Tenant Admin / HR Officer) as the other leave-config screens.
    path: 'holidays',
    loadComponent: () =>
      import('./components/holiday-calendar/holiday-calendar.component').then(
        (m) => m.HolidayCalendarComponent
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
    // US-LV-006: Employee's Leave Balance Dashboard — the default landing
    // view within the Leave module for the Employee persona (§10).
    path: 'dashboard',
    loadComponent: () =>
      import('./components/leave-dashboard/leave-dashboard.component').then(
        (m) => m.LeaveDashboardComponent
      ),
  },
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
  {
    // US-LV-004: Manager's pending leave-approval queue.
    // The parent '/leave' route already requires authentication + one of
    // Employee/Manager/HR Officer/Tenant Admin; this child further restricts
    // to leave-approver roles (Manager / HR Officer / Tenant Admin) per
    // the Leave.Approve.Team capability.
    path: 'approvals',
    canActivate: [roleGuard(['Manager', 'HR Officer', 'Tenant Admin'])],
    loadComponent: () =>
      import('./components/leave-approvals/leave-approvals.component').then(
        (m) => m.LeaveApprovalsComponent
      ),
  },
  // US-LV-006: the dashboard is the Employee landing view for the Leave module (§10).
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
];
