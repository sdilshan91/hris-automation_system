import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { MainLayoutComponent } from './main-layout.component';
import { AuthService } from '../../core/auth/auth.service';
import { ImpersonationBannerComponent } from '../../features/admin/impersonation/components/impersonation-banner/impersonation-banner.component';
import { NotificationBellComponent } from '../../features/notifications/components/notification-bell/notification-bell.component';
import { IdleTimeoutWarningComponent } from '../../shared/components/idle-timeout-warning/idle-timeout-warning.component';

/**
 * ISSUE-208 (MED, Attendance, US-ATT-003..009) — attendance sub-pages orphaned from nav.
 * TC id: TC-ATT-144 (nav-discoverability regression; free suffix in the attendance set).
 *
 * Pre-fix the sidebar exposes a SINGLE flat "Attendance" → `/attendance` item
 * (`main-layout.component.ts` — one navItem, `permission: 'Attendance.View.Own'`,
 * no children). Every other attendance route (`regularization`,
 * `regularization-approvals`, `shifts`, `overtime`, `overtime-approvals`,
 * `overtime-report`, `monthly-summary`, `lateness-score`, `late-early-report`,
 * `late-policy`, `payroll-integration`, `dashboard`, `reports` — see
 * `attendance.routes.ts`) is route-guard-reachable but has ZERO in-app nav entry,
 * so the bulk of the module is undiscoverable through normal navigation.
 *
 * The fix adds nav entries for those sub-pages, each gated to the persona whose
 * attendance route-guard admits it (the sub-routes role-guard: self views —
 * overtime/lateness-score/regularization — inherit the base employee guard; the
 * approver + HR views — shifts/approvals/monthly-summary/late-policy/etc. — restrict
 * to Manager / HR Officer / HR Manager / Tenant Admin).
 *
 * The assertions are written on the OUTCOME (which attendance routes appear in the
 * rendered nav for which persona), NOT on the gating mechanism, so they hold whether
 * the fix gates each item by permission or by role. Because the exact set of sub-routes
 * the fix exposes is decided by the parallel `main-layout.component.ts` change, the
 * crisp invariant is keyed on "≥1 attendance SUB-route beyond `/attendance` is present
 * for a privileged persona" and cross-checked against the known attendance child paths;
 * concrete expected routes are named via candidate sets (adjust the candidate lists to
 * the sub-routes the fix actually added once it lands in the working tree).
 *
 * Each persona below carries BOTH realistic roles and the documented Attendance.*
 * permissions so it passes whichever gate the fix chooses.
 *
 * Stub child components keep this a focused nav test (the real banner/bell/idle
 * components pull SignalR/notification deps that are irrelevant to nav filtering).
 */
@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

/**
 * Every real attendance CHILD path from `attendance.routes.ts` (i.e. everything
 * under `/attendance/…`). A rendered attendance sub-link must be one of these — a
 * nav item pointing anywhere else under `/attendance/` would be a dead/typo link.
 */
const ATTENDANCE_SUBROUTES = [
  '/attendance/clock-in',
  '/attendance/regularization',
  '/attendance/regularization-approvals',
  '/attendance/shifts',
  '/attendance/overtime',
  '/attendance/overtime-approvals',
  '/attendance/overtime-report',
  '/attendance/monthly-summary',
  '/attendance/lateness-score',
  '/attendance/late-early-report',
  '/attendance/late-policy',
  '/attendance/payroll-integration',
  '/attendance/dashboard',
  '/attendance/reports',
];

/**
 * Sub-routes an HR/manager persona can ENTER (their child roleGuard admits Manager /
 * HR Officer / HR Manager / Tenant Admin). The fix should surface at least one of
 * these for a privileged persona; naming several keeps the assertion robust to which
 * exact ones the parallel fix chose to add.
 */
const HR_REACHABLE_SUBROUTES = [
  '/attendance/shifts',
  '/attendance/regularization-approvals',
  '/attendance/overtime-approvals',
  '/attendance/monthly-summary',
  '/attendance/late-policy',
  '/attendance/dashboard',
  '/attendance/reports',
];

/**
 * Employee-facing self sub-routes (inherit the base attendance guard — any
 * authenticated employee). The fix should surface at least one of these for an
 * employee persona.
 */
const EMPLOYEE_REACHABLE_SUBROUTES = [
  '/attendance/overtime',
  '/attendance/lateness-score',
  '/attendance/regularization',
];

/**
 * HR-only sub-routes an EMPLOYEE cannot enter (their child roleGuard excludes
 * Employee, so a nav link would dead-end at `/forbidden`). None of these may be
 * shown to an employee persona.
 */
const EMPLOYEE_FORBIDDEN_SUBROUTES = [
  '/attendance/shifts',
  '/attendance/monthly-summary',
  '/attendance/late-policy',
  '/attendance/regularization-approvals',
];

describe('MainLayoutComponent attendance nav visibility (ISSUE-208 / TC-ATT-144)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

  /**
   * Build a real principal by driving the REAL AuthService through its own token
   * path: activateImpersonation() decodes the JWT and sets both the roles (used by
   * hasRole) and the permissions signal (used by hasPermission/hasAnyPermission). No
   * reimplementation of the nav filter — the component's own visibleNavItems() runs.
   */
  function loginAs(roles: string[], permissions: string[]): void {
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const payload = btoa(
      JSON.stringify({
        sub: 'user-1',
        roles,
        permissions,
        exp: Math.floor(Date.now() / 1000) + 3600,
      })
    );
    authService.activateImpersonation(`${header}.${payload}.sig`);
  }

  /** The routes actually rendered as sidebar nav links (from the DOM href). */
  function renderedRoutes(): (string | null)[] {
    return fixture.debugElement
      .queryAll(By.css('nav.sidebar-nav a.nav-item'))
      .map((de) => de.nativeElement.getAttribute('href'));
  }

  /** Rendered nav links that point at an attendance CHILD route (`/attendance/…`). */
  function renderedAttendanceSubRoutes(): string[] {
    return renderedRoutes().filter(
      (r): r is string => !!r && r.startsWith('/attendance/')
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MainLayoutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        {
          provide: ToastrService,
          useValue: jasmine.createSpyObj<ToastrService>('ToastrService', [
            'info',
            'warning',
            'success',
            'error',
          ]),
        },
      ],
    });

    // Swap the heavy child components for inert stubs (same selectors) so the
    // layout template still renders but their deps aren't required.
    TestBed.overrideComponent(MainLayoutComponent, {
      remove: {
        imports: [
          ImpersonationBannerComponent,
          NotificationBellComponent,
          IdleTimeoutWarningComponent,
        ],
      },
      add: {
        imports: [
          StubImpersonationBannerComponent,
          StubNotificationBellComponent,
          StubIdleTimeoutWarningComponent,
        ],
      },
    });

    authService = TestBed.inject(AuthService);
    // ngOnInit loads the tenant switcher + idle-timeout settings over HTTP — stub
    // both so the test stays offline and focused on nav rendering.
    spyOn(authService, 'getMyTenants').and.returnValue(of([]));
    spyOn(authService, 'getTenantAuthSettings').and.returnValue(
      of({ idleTimeoutMinutes: 0 }) as ReturnType<AuthService['getTenantAuthSettings']>
    );
  });

  it('nav_exposes_attendance_subpages_ISSUE208: HR/manager sees attendance sub-routes, not just /attendance', () => {
    // Attendance-privileged persona: an HR principal who can ENTER the approver + HR
    // attendance child routes (Manager / HR Officer / HR Manager / Tenant Admin), and
    // holds the attendance team/all permissions. Carries both roles and permissions so
    // it passes whichever gate the fix uses.
    loginAs(
      ['HR Officer', 'HR Manager', 'Tenant Admin'],
      [
        'Attendance.View.Own',
        'Attendance.View.Team',
        'Attendance.View.All',
        'Attendance.Read.All',
        'Attendance.Approve.Team',
        'Attendance.Shift.Manage',
      ]
    );

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const subRoutes = renderedAttendanceSubRoutes();

    // Crisp invariant — pre-fix this FAILS: the only attendance nav entry is the flat
    // `/attendance`, so there are ZERO `/attendance/…` sub-route links. Post-fix the
    // sub-pages this HR persona can enter are reachable through nav.
    expect(subRoutes.length)
      .withContext('expected ≥1 attendance sub-route link beyond the flat /attendance')
      .toBeGreaterThan(0);

    // Every rendered attendance sub-link must be a real attendance child route
    // (no dead/typo links introduced by the fix).
    subRoutes.forEach((r) =>
      expect(ATTENDANCE_SUBROUTES)
        .withContext(`rendered attendance sub-link "${r}" is not a known attendance child route`)
        .toContain(r)
    );

    // …and at least one of them is an HR-reachable page the fix should surface
    // (e.g. /attendance/shifts or /attendance/regularization-approvals).
    expect(subRoutes.some((r) => HR_REACHABLE_SUBROUTES.includes(r)))
      .withContext(`expected one of ${HR_REACHABLE_SUBROUTES.join(', ')} in ${subRoutes.join(', ')}`)
      .toBeTrue();
  });

  it('nav_exposes_employee_attendance_subpages_ISSUE208: employee sees reachable self sub-routes, not an HR-only page', () => {
    // Employee persona: base self-service attendance (Attendance.View.Own) plus the
    // self clock/overtime/regularization capability. Cannot enter the HR/approver
    // child routes (their roleGuard excludes Employee).
    loginAs(
      ['Employee'],
      ['Attendance.View.Own', 'Attendance.CheckIn', 'Attendance.Regularize.Self']
    );

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    const subRoutes = renderedAttendanceSubRoutes();

    // ISSUE-208 fix design: the single flat `/attendance` redirect item is REPLACED by
    // granular persona-gated sub-items, so the employee's attendance entry point is now
    // "Clock In" (/attendance/clock-in), not a bare `/attendance` link. The module is still
    // reachable — just via a concrete sub-page.
    expect(routes)
      .withContext('employee should reach attendance via the Clock In sub-page')
      .toContain('/attendance/clock-in');

    // Pre-fix this FAILS: the employee sees only `/attendance`, so none of their own
    // reachable sub-pages (overtime / lateness-score / regularization) are in the nav.
    expect(subRoutes.some((r) => EMPLOYEE_REACHABLE_SUBROUTES.includes(r)))
      .withContext(`expected one of ${EMPLOYEE_REACHABLE_SUBROUTES.join(', ')} in ${subRoutes.join(', ')}`)
      .toBeTrue();

    // Guard-safety (mirrors the ISSUE-210 invariant): the employee must NOT be handed
    // an HR-only attendance link that its route guard would bounce to /forbidden.
    expect(routes)
      .withContext('employee must not see the HR-only /attendance/shifts link')
      .not.toContain('/attendance/shifts');
  });

  it('no persona is shown an attendance nav link its route guard would reject — employee sees no HR-only attendance page (ISSUE-208)', () => {
    // The crisp guard invariant, isolated: an employee-only principal must never see
    // any of the manager/HR-only attendance child links (each would dead-end at
    // /forbidden). This holds pre- and post-fix and guards the fix against
    // over-exposing HR pages to employees.
    loginAs(['Employee'], ['Attendance.View.Own', 'Attendance.CheckIn']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    EMPLOYEE_FORBIDDEN_SUBROUTES.forEach((hrRoute) =>
      expect(routes)
        .withContext(`employee must not see HR-only attendance link ${hrRoute}`)
        .not.toContain(hrRoute)
    );
  });
});
