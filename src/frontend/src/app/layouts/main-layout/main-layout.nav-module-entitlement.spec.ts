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
import { TenantService } from '../../core/tenant/tenant.service';
import { ImpersonationBannerComponent } from '../../features/admin/impersonation/components/impersonation-banner/impersonation-banner.component';
import { NotificationBellComponent } from '../../features/notifications/components/notification-bell/notification-bell.component';
import { IdleTimeoutWarningComponent } from '../../shared/components/idle-timeout-warning/idle-timeout-warning.component';

/**
 * US-ADM-012 AC-2 — module-entitlement nav filtering.
 *
 * A module-tagged sidebar item (e.g. Payroll) must be HIDDEN when the tenant's plan
 * does not entitle its module, while an UNTAGGED item (e.g. Departments — CoreHR /
 * always-on) is never module-gated. Entitlement fails OPEN, so a legacy/non-canonical
 * module list must not hide anything.
 *
 * These assert on the rendered DOM (which routes appear as nav links) so they hold
 * regardless of the internal filter mechanism. The naive implementation this guards
 * against is a bare `enabledModules.includes(module)` check — noted per arm.
 *
 * Stub child components keep this a focused nav test (mirrors the sibling
 * main-layout.nav-visibility.spec.ts rationale).
 */
@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

const LEGACY_VOCAB = [
  'Attendance',
  'Audit',
  'Benefits',
  'CustomField',
  'Department',
  'Employee',
  'Leave',
  'Payroll',
  'Reports',
  'Roles',
  'Tenant',
  'Training',
];

describe('MainLayoutComponent nav module entitlement (US-ADM-012 AC-2)', () => {
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;
  let tenantService: TenantService;

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

  function setEnabledModules(modules: string[]): void {
    tenantService.tenantContext.set({
      subdomain: 'acme',
      isSystemContext: false,
      isReserved: false,
      isValid: true,
      state: 'resolved',
      enabledModules: modules,
    });
  }

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
    tenantService = TestBed.inject(TenantService);
    spyOn(authService, 'getMyTenants').and.returnValue(of([]));
    spyOn(authService, 'getTenantAuthSettings').and.returnValue(
      of({ idleTimeoutMinutes: 0 }) as ReturnType<AuthService['getTenantAuthSettings']>
    );

    // A tenant principal that HOLDS the permissions for several module-tagged items
    // (Payroll.View → Payroll, Recruitment.View → Recruitment) and an entitled one
    // (Leave.View.Own → Leave), so visibility is decided by module entitlement, not
    // permission.
    loginAs(
      ['Tenant Admin'],
      ['Payroll.View', 'Recruitment.View', 'Leave.View.Own']
    );
  });

  it('hides a non-entitled module item but keeps an untagged item and an entitled one', () => {
    // Authoritative canonical list: enables Leave, NOT Payroll.
    setEnabledModules(['CoreHR', 'Leave']);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    // Discriminates: with the module filter, /payroll is hidden even though the user
    // holds Payroll.View. A permission-only nav (pre-fix) would still render it.
    expect(routes).not.toContain('/payroll');
    // Untagged CoreHR item is never module-gated.
    expect(routes).toContain('/departments');
    // Entitled + permitted item stays.
    expect(routes).toContain('/leave');
  });

  it('FAILS-OPEN: legacy/non-canonical module list hides nothing — /recruitment stays', () => {
    // THE discriminating arm. 'Recruitment' is NOT a token in LEGACY_VOCAB, so a naive
    // `enabledModules.includes('Recruitment')` nav filter would HIDE /recruitment and
    // this test would FAIL. Because the list carries non-canonical tokens (Audit,
    // CustomField, Department, …) it is not authoritative → nothing is hidden.
    setEnabledModules(LEGACY_VOCAB);

    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();

    const routes = renderedRoutes();
    expect(routes).toContain('/recruitment');
    expect(routes).toContain('/payroll');
    expect(routes).toContain('/leave');
    expect(routes).toContain('/departments');
  });
});
