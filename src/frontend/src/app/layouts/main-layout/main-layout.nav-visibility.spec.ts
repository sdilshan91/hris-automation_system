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
 * ISSUE-210 (HIGH, Performance Management, US-PRF-001..010) — sidebar nav dead-ends.
 * TC id: TC-PRF-001-13 (nav-visibility regression; free suffix in the US-PRF-001 set).
 *
 * The single "Performance" sidebar item was gated on `Performance.View.Own` (an
 * employee permission) but routes to `/performance`, which is role-guarded to
 * Manager/HR/Admin. Consequences pre-fix:
 *   - the employee who SEES the link cannot enter it → dead-ends at /forbidden;
 *   - HR/managers (who can enter it) never see the link;
 *   - the employee self-service `/my-review` tree has NO nav entry at all.
 *
 * The fix splits this into two persona-scoped items:
 *   - a management "Performance" item → `/performance`, visible to holders of
 *     Performance.View.Team / .View.All / .Manage (Manager/HR/Admin), NOT employees;
 *   - a "My Performance"/"My Review" item → `/my-review`, visible to employees
 *     (Performance.View.Own).
 *
 * Crisp invariant guarded here: no persona is ever shown a nav link to a route
 * its guard would reject — specifically an employee-only principal must NOT see a
 * `/performance` link.
 *
 * The assertions are written on the OUTCOME (which persona sees which route in the
 * rendered nav), not on the gating mechanism, so they hold whether the fix gates by
 * permission array or by role — the persona below carries BOTH realistic roles and
 * the documented Performance.* permissions.
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

describe('MainLayoutComponent nav visibility (ISSUE-210 / TC-PRF-001-13)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

  /**
   * Build a real principal by driving the REAL AuthService through its own token
   * path: activateImpersonation() decodes the JWT and sets roles (used by hasRole)
   * and the permissions signal (used by hasPermission). No reimplementation of the
   * nav filter — the component's own visibleNavItems() runs for real.
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

  it('nav shows /performance to HR/manager, not employee (ISSUE-210)', () => {
    // Management principal: can ENTER /performance (Manager/HR/Admin guard),
    // holds Performance.View.Team/.All/.Manage but NOT Performance.View.Own.
    loginAs(
      ['HR Manager'],
      [
        'Employee.View.All',
        'Performance.View.All',
        'Performance.View.Team',
        'Performance.Manage',
      ]
    );

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    // Pre-fix this FAILS: the only Performance item is gated on Performance.View.Own,
    // which this manager lacks, so no /performance link is rendered for someone who
    // can actually enter it.
    expect(routes).toContain('/performance');
    // The self-service item is for employees (View.Own) — a manager without View.Own
    // must not be handed it.
    expect(routes).not.toContain('/my-review');
  });

  it('nav shows /my-review to employee, not /performance (ISSUE-210)', () => {
    // Employee principal: Performance.View.Own only — cannot enter /performance.
    loginAs(
      ['Employee'],
      ['Leave.View.Own', 'Attendance.View.Own', 'Performance.View.Own']
    );

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    // Pre-fix this FAILS: there is no /my-review nav entry at all.
    expect(routes).toContain('/my-review');
    // Pre-fix this ALSO FAILS: the View.Own-gated Performance item renders and
    // routes the employee straight to a link the /performance guard rejects.
    expect(routes).not.toContain('/performance');
  });

  it('no persona is shown a nav link its route guard would reject — employee-only sees no /performance (ISSUE-210)', () => {
    // The crisp invariant, isolated: an employee-only principal (no View.Team/.All/
    // .Manage) must never see the management /performance link that dead-ends at
    // /forbidden.
    loginAs(['Employee'], ['Performance.View.Own']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    expect(renderedRoutes()).not.toContain('/performance');
  });

  // ── BUG-450 ────────────────────────────────────────────────────────────────────────────
  // Departments and Job Titles shipped with NO gate at all — not the wrong gate, none. The
  // filter reads `if (!item.permission) return true`, so an ungated item is visible to
  // EVERYONE, and both routes are roleGuard(['Tenant Admin','HR Officer']). Every Employee
  // and Manager in every tenant was shown two prominent links that dead-end at /forbidden.
  //
  // This is asserted as an INVARIANT over the whole nav rather than as two route names,
  // because the defect class is "an item with no gate", not "these two items". A test naming
  // /departments and /job-titles would go green the moment someone adds a third ungated
  // item — which is exactly how this one survived: the sibling arm below pins /performance
  // alone, and these two sat beside it unnoticed.
  const CORE_HR_ADMIN_ROUTES = ['/departments', '/job-titles'];

  it('an employee-only principal sees no Core-HR master-data link (BUG-450)', () => {
    loginAs(['Employee'], ['Employee.View.Own']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const shown = renderedRoutes();
    for (const route of CORE_HR_ADMIN_ROUTES) {
      expect(shown)
        .withContext(
          `${route} is guarded by roleGuard(['Tenant Admin','HR Officer']); showing it to an ` +
            'Employee is a link that dead-ends at /forbidden (BUG-450)'
        )
        .not.toContain(route);
    }
  });

  it('a Manager still sees no Core-HR master-data link (BUG-450)', () => {
    // Manager is the persona most likely to be mistaken for an admin. The route guard does
    // not admit it, so the nav must not either.
    loginAs(['Manager'], ['Employee.View.Team']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const shown = renderedRoutes();
    for (const route of CORE_HR_ADMIN_ROUTES) {
      expect(shown).not.toContain(route);
    }
  });

  it('HR Officer and Tenant Owner both DO see them — the gate must not overshoot (BUG-450)', () => {
    // The failure mode opposite to the bug: a gate that hides the link from someone who can
    // enter. 'Tenant Owner' is in the list because roleGuard implicitly widens every tenant
    // guard with TENANT_SUPER_ROLES (auth.guard.ts:63,76), so a nav gate omitting it would
    // hide a working page from the tenant's owner.
    for (const role of ['HR Officer', 'Tenant Owner']) {
      loginAs([role], []);
      fixture = TestBed.createComponent(MainLayoutComponent);
      fixture.detectChanges();

      const shown = renderedRoutes();
      for (const route of CORE_HR_ADMIN_ROUTES) {
        expect(shown)
          .withContext(`${role} passes the route guard, so the nav must show ${route}`)
          .toContain(route);
      }
    }
  });
});
