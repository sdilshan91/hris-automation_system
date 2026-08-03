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
 * ISSUE-214 — Notification Templates (US-NTF-002) and Notification Preferences
 * (US-NTF-003) shipped reachable by direct URL ONLY: 46 sidebar entries and neither
 * page among them, so two built features were effectively invisible.
 *
 * The arms that matter:
 *   - Templates renders for a persona the route's roleGuard(['Tenant Admin','Tenant
 *     Owner']) would admit, and NOT for one it would bounce. A link a user cannot
 *     follow is a support ticket, not a feature (the ISSUE-210 rule: nav visibility
 *     == route access).
 *   - Preferences renders for EVERY authenticated persona, because its route carries
 *     no roleGuard — each user manages their own. An over-gated entry would be the
 *     same invisibility defect in a new place.
 *
 * Assertions are on the rendered nav OUTCOME (hrefs in the DOM), driving the
 * component's real visibleNavItems() through the REAL AuthService — the nav filter is
 * exercised, not reimplemented.
 */
@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

const TEMPLATES_ROUTE = '/admin/notification-templates';
const PREFERENCES_ROUTE = '/profile/notification-preferences';

describe('MainLayoutComponent notification nav entries (ISSUE-214)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

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

  function renderedRoutes(): (string | null)[] {
    return fixture.debugElement
      .queryAll(By.css('nav.sidebar-nav a.nav-item'))
      .map((de) => de.nativeElement.getAttribute('href'));
  }

  function render(): void {
    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
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
    spyOn(authService, 'getMyTenants').and.returnValue(of([]));
    spyOn(authService, 'getTenantAuthSettings').and.returnValue(
      of({ idleTimeoutMinutes: 0 }) as ReturnType<AuthService['getTenantAuthSettings']>
    );
  });

  it('shows Notification Templates to a Tenant Admin (the route guard admits them)', () => {
    loginAs(['Tenant Admin'], ['Tenant.ManageSettings']);
    render();

    expect(renderedRoutes())
      .withContext('a Tenant Admin can enter the templates route, so it must be discoverable')
      .toContain(TEMPLATES_ROUTE);
  });

  it('shows Notification Templates to a Tenant Owner', () => {
    loginAs(['Tenant Owner'], []);
    render();

    expect(renderedRoutes()).toContain(TEMPLATES_ROUTE);
  });

  it('hides Notification Templates from a persona the route guard would bounce', () => {
    // An HR Officer holds real tenant permissions but is NOT in the templates
    // roleGuard list — surfacing the link would produce a dead end.
    loginAs(['HR Officer'], ['Leave.View.All', 'Employee.View.All']);
    render();

    expect(renderedRoutes())
      .withContext('nav visibility must not exceed route access (ISSUE-210)')
      .not.toContain(TEMPLATES_ROUTE);
  });

  it('shows Notification Preferences to every authenticated persona', () => {
    // Preferences carries no roleGuard: a plain employee manages their own.
    loginAs(['Employee'], []);
    render();

    expect(renderedRoutes())
      .withContext('an ungated personal-settings page must be reachable by everyone')
      .toContain(PREFERENCES_ROUTE);
  });

  it('shows Notification Preferences to an admin persona too', () => {
    loginAs(['Tenant Admin'], ['Tenant.ManageSettings']);
    render();

    expect(renderedRoutes()).toContain(PREFERENCES_ROUTE);
  });
});
