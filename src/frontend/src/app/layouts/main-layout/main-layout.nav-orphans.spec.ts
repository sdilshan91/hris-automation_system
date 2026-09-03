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
 * ISSUE-372 / fix-queue E5 — sidebar NAV ORPHANS.
 * TC id: TC-CHR-006-11 (nav-orphan regression; free suffix in the US-CHR-006 set).
 *
 * Three built, wired, backend-backed Core HR pages shipped reachable by URL only —
 * `grep` for each against main-layout.component.ts returned zero mentions, so the only
 * way in was to type the address:
 *   - /locations              (US-CHR-007, roleGuard(['Tenant Admin','HR Officer']))
 *   - /org-tree               (US-CHR-006, roleGuard(['Tenant Admin','HR Officer','Manager']))
 *   - /settings/custom-fields (US-CHR-012, roleGuard(['Tenant Admin']))
 *
 * The fix adds one nav item per route, gated with `tenantRoles` — the ISSUE-214
 * mechanism that mirrors a `roleGuard` exactly. Gating them on a permission PROXY
 * (e.g. Tenant.ManageSettings for custom fields) is what produced ISSUE-210: nav
 * visibility drifts from route access and the link 403s for whoever it is shown to.
 *
 * Two gate details this spec deliberately pins, because both are easy to get wrong:
 *
 *  1. TENANT OWNER. `roleGuard` implicitly widens EVERY tenant guard with
 *     'Tenant Owner' (auth.guard.ts TENANT_SUPER_ROLES), so a nav gate that lists only
 *     the literal role array hides the link from a persona that can enter the route —
 *     the inverse ISSUE-210 defect (link never shown to someone entitled to it).
 *
 *  2. PER-ROUTE ROLE SETS ARE NOT INTERCHANGEABLE. Manager may enter /org-tree but NOT
 *     /locations; HR Officer may enter /locations but NOT /settings/custom-fields.
 *     Copy-pasting one gate onto all three would pass a naive "the link renders" test
 *     and still ship the exact defect this file exists to prevent — so each persona is
 *     asserted on both the routes it SHOULD see and the ones it must NOT.
 *
 * Assertions are on the OUTCOME (which persona sees which href in the rendered nav),
 * not on the gating mechanism, so they survive a refactor of how gating is expressed.
 *
 * Stub child components keep this a focused nav test, matching the sibling
 * main-layout.nav-visibility.spec.ts (the real banner/bell/idle components pull
 * SignalR/notification deps irrelevant to nav filtering).
 */
@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

describe('MainLayoutComponent nav orphans (ISSUE-372 E5 / TC-CHR-006-11)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

  /**
   * Build a real principal by driving the REAL AuthService through its own token path:
   * activateImpersonation() decodes the JWT and sets the roles claim (used by hasRole)
   * and the permissions signal (used by hasPermission). The component's own
   * visibleNavItems() then runs for real — nothing about the filter is reimplemented here.
   */
  function loginAs(roles: string[], permissions: string[] = []): void {
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

  /** Log in, render the shell, and return the rendered nav hrefs. */
  function navFor(roles: string[], permissions: string[] = []): (string | null)[] {
    loginAs(roles, permissions);
    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    return renderedRoutes();
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
    // ngOnInit loads the tenant switcher + idle-timeout settings over HTTP — stub both
    // so the test stays offline and focused on nav rendering.
    spyOn(authService, 'getMyTenants').and.returnValue(of([]));
    spyOn(authService, 'getTenantAuthSettings').and.returnValue(
      of({ idleTimeoutMinutes: 0 }) as ReturnType<AuthService['getTenantAuthSettings']>
    );
  });

  // ── /locations (US-CHR-007) ───────────────────────────────────────────────
  it('nav shows /locations to HR Officer (entitled) and not to Employee (unentitled)', () => {
    // Pre-fix BOTH halves are wrong in the same direction: there is no /locations nav
    // item at all, so the entitled HR Officer never sees the page that was built for them.
    expect(navFor(['HR Officer'])).toContain('/locations');
    expect(navFor(['Employee'], ['Employee.View.Own'])).not.toContain('/locations');
  });

  // ── /org-tree (US-CHR-006) ────────────────────────────────────────────────
  it('nav shows /org-tree to Manager (entitled) and not to Employee (unentitled)', () => {
    // Manager is in the /org-tree roleGuard but NOT the /locations one — asserting both
    // in one persona is what catches a copy-pasted gate.
    const managerNav = navFor(['Manager'], ['Employee.View.Team']);
    expect(managerNav).toContain('/org-tree');
    expect(managerNav).not.toContain('/locations');

    expect(navFor(['Employee'], ['Employee.View.Own'])).not.toContain('/org-tree');
  });

  // ── /settings/custom-fields (US-CHR-012) ──────────────────────────────────
  it('nav shows /settings/custom-fields to Tenant Admin and not to HR Officer', () => {
    expect(navFor(['Tenant Admin'])).toContain('/settings/custom-fields');
    // HR Officer can enter /locations and /org-tree but the custom-fields route is
    // roleGuard(['Tenant Admin']) — an HR-wide gate here would be a link that 403s.
    const hrNav = navFor(['HR Officer']);
    expect(hrNav).not.toContain('/settings/custom-fields');
    expect(hrNav).toContain('/org-tree');
  });

  // ── Tenant Owner: roleGuard's implicit TENANT_SUPER_ROLES widening ────────
  it('nav shows all three to Tenant Owner, whom roleGuard implicitly admits', () => {
    // auth.guard.ts adds 'Tenant Owner' to every non-system roleGuard, so the Owner can
    // enter all three routes. A nav gate listing only the literal role array would hide
    // every one of these links from them — the inverse ISSUE-210 defect.
    const ownerNav = navFor(['Tenant Owner']);
    expect(ownerNav).toContain('/locations');
    expect(ownerNav).toContain('/org-tree');
    expect(ownerNav).toContain('/settings/custom-fields');
  });

  // ── /offboarding: deliberately NOT given a nav entry ──────────────────────
  it('nav offers no bare /offboarding link, which resolves to nothing', () => {
    // OFFBOARDING_ROUTES declares only 'initiate/:employeeId' and ':offboardingId' — there
    // is no `path: ''` index — so bare /offboarding matches no route, falls through to the
    // `**` wildcard in app.routes.ts and REDIRECTS AN AUTHENTICATED USER TO THE LOGIN PAGE.
    // Adding the "obvious" fourth link would therefore ship a worse dead end than the
    // orphan it replaced. The offboarding feature needs a real entry point (an index page
    // behind a backend list endpoint, or a contextual action on the employee profile);
    // until then this asserts nobody re-adds the broken link by reflex.
    for (const roles of [['Tenant Admin'], ['HR Officer'], ['Tenant Owner'], ['Employee']]) {
      expect(navFor(roles)).not.toContain('/offboarding');
    }
  });
});
