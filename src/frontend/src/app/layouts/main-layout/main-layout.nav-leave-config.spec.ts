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
 * DEC-D5 — discoverable "Leave configuration" nav group.
 *
 * The four leave-admin screens under the '/leave-types' tree (entitlement rules,
 * holiday calendar, carry-forward preview, accrual over-credit review) were reachable
 * only by direct URL — no sidebar entry existed. This regression guards that:
 *   - the group renders for an entitled persona (holds Leave.ConfigurePolicy);
 *   - it does NOT render for a persona the '/leave-types' route guard would bounce
 *     (an employee-only principal) — the arm that matters, since a link users cannot
 *     follow is a support ticket, not a feature;
 *   - each of the four entries points at its REAL route path (an arm that fails on a
 *     mistyped destination).
 *
 * Assertions are on the rendered nav OUTCOME (route hrefs in the DOM), driving the
 * component's own visibleNavItems() through the REAL AuthService — no reimplementation
 * of the nav filter. Heavy child components are stubbed (their SignalR/notification
 * deps are irrelevant to nav filtering).
 */
@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

/** The four leave-config destinations (the actual '/leave-types' child route paths). */
const LEAVE_CONFIG_ROUTES = [
  '/leave-types/entitlements',
  '/leave-types/holidays',
  '/leave-types/carry-forward-preview',
  '/leave-types/accrual-over-credit-exposure',
];

describe('MainLayoutComponent leave-config nav group (DEC-D5)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

  /**
   * Drive the REAL AuthService through its own token path: activateImpersonation()
   * decodes the JWT and populates roles (hasRole) and the permissions signal
   * (hasPermission), which visibleNavItems() then consumes for real.
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

  it('renders the leave-config group for an entitled HR persona (holds Leave.ConfigurePolicy)', () => {
    // HR Officer: can ENTER '/leave-types' (roleGuard Tenant Admin/HR Officer) and
    // holds the Leave.ConfigurePolicy capability these screens consume.
    loginAs(['HR Officer'], ['Leave.View.All', 'Leave.ConfigurePolicy']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    LEAVE_CONFIG_ROUTES.forEach((r) =>
      expect(routes)
        .withContext(`entitled HR persona should see ${r}`)
        .toContain(r)
    );
  });

  it('does NOT render the leave-config group for an employee-only persona (cannot reach the routes)', () => {
    // Employee: lacks Leave.ConfigurePolicy and is not admitted by the '/leave-types'
    // roleGuard. Showing any of these links would dead-end at /forbidden.
    loginAs(['Employee'], ['Leave.View.Own', 'Leave.Apply']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    LEAVE_CONFIG_ROUTES.forEach((r) =>
      expect(routes)
        .withContext(`employee-only persona must NOT see ${r}`)
        .not.toContain(r)
    );
  });

  it('each leave-config entry points at its real route path', () => {
    loginAs(['Tenant Admin'], ['Leave.ConfigurePolicy']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    // Exactly the four documented destinations render — no missing and no extra/typo
    // '/leave-types/…' link is introduced by the group.
    const leaveConfigLinks = routes.filter((r) => r?.startsWith('/leave-types'));
    expect(leaveConfigLinks.sort()).toEqual([...LEAVE_CONFIG_ROUTES].sort());
  });

  // HR Manager: holds Leave.ConfigurePolicy in PermissionCatalog, so the nav shows — and as of 2026-08-02
  // the leave-types route guard admits them too. Before that fix this persona saw the links and was bounced
  // to /forbidden. This arm pins the nav side of that agreement; the guard side lives in app.routes.ts.
  it('renders the leave-config group for an HR Manager (permission holder, now route-admitted)', () => {
    loginAs(['HR Manager'], ['Leave.View.All', 'Leave.ConfigurePolicy']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    LEAVE_CONFIG_ROUTES.forEach((r) =>
      expect(routes)
        .withContext(
          `HR Manager holds Leave.ConfigurePolicy and, since the 2026-08-02 guard fix, can enter ` +
            `'/leave-types' — so ${r} must be visible. Before that fix this persona saw the links and was ` +
            `bounced to /forbidden, which is worse than no link at all.`
        )
        .toContain(r)
    );
  });
});
