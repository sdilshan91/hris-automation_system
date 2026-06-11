import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { environment } from '../../../environments/environment';
import { TenantService } from '../tenant/tenant.service';
import { AuthService } from './auth.service';
import { ITokenClaims } from './auth.models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let tenantService: jasmine.SpyObj<TenantService>;

  beforeEach(() => {
    tenantService = jasmine.createSpyObj<TenantService>('TenantService', [
      'setTenantFromAuth',
    ]);

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        {
          provide: ToastrService,
          useValue: jasmine.createSpyObj<ToastrService>('ToastrService', [
            'info',
            'warning',
          ]),
        },
        { provide: TenantService, useValue: tenantService },
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads tenant memberships with credentials for the switcher', () => {
    service.getMyTenants().subscribe((tenants) => {
      expect(tenants.length).toBe(2);
      expect(tenants[0].name).toBe('Acme HR');
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/my-tenants`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([
      {
        tenantId: 'tenant-a',
        subdomain: 'acme',
        name: 'Acme HR',
        status: 'active',
        roles: ['Tenant Admin'],
        isCurrentTenant: true,
      },
      {
        tenantId: 'tenant-b',
        subdomain: 'bravo',
        name: 'Bravo Payroll',
        status: 'trial',
        roles: ['Auditor'],
        isCurrentTenant: false,
      },
    ]);
  });

  // ─── Session management (US-AUTH-009) ────────────────────

  it('fetches user own sessions with credentials', () => {
    const mockSessions = [
      {
        sessionId: 's1',
        device: 'Desktop',
        browser: 'Chrome',
        os: 'Windows',
        ipAddress: '1.2.3.4',
        issuedAt: '2026-06-01T00:00:00Z',
        lastActiveAt: '2026-06-01T01:00:00Z',
        isCurrent: true,
      },
    ];

    service.getMySessions().subscribe((sessions) => {
      expect(sessions.length).toBe(1);
      expect(sessions[0].sessionId).toBe('s1');
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/me/sessions`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockSessions);
  });

  it('revokes user own session with credentials', () => {
    service.revokeSession('s1').subscribe((resp) => {
      expect(resp.message).toBe('Revoked');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/auth/me/sessions/s1/revoke`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ message: 'Revoked' });
  });

  it('fetches admin user sessions with credentials', () => {
    service.getUserSessions('user-abc').subscribe((sessions) => {
      expect(sessions.length).toBe(0);
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users/user-abc/sessions`
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([]);
  });

  it('revokes a specific admin user session', () => {
    service.revokeUserSession('user-abc', 's2').subscribe((resp) => {
      expect(resp.message).toBe('Revoked');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users/user-abc/sessions/revoke`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ sessionId: 's2' });
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ message: 'Revoked' });
  });

  it('revokes all admin user sessions (no sessionId)', () => {
    service.revokeUserSession('user-abc').subscribe((resp) => {
      expect(resp.message).toBe('All revoked');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users/user-abc/sessions/revoke`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ message: 'All revoked' });
  });

  it('sends keep-alive request with credentials', () => {
    service.keepAlive().subscribe((resp) => {
      expect(resp.message).toBe('ok');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/auth/me/keep-alive`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ message: 'ok' });
  });

  // ─── Tenant switch ──────────────────────────────────────

  it('replaces tenant-scoped claims before redirecting after a switch', () => {
    const redirectSpy = spyOn<any>(service, 'redirectTo');
    const accessToken = tokenFor({
      tenant_id: 'tenant-b',
      roles: ['Auditor'],
      permissions: ['Payroll.View'],
    });

    service.switchTenant({ tenantId: 'tenant-b' }).subscribe((response) => {
      expect(response.redirectUrl).toBe('https://bravo.yourhrm.com/dashboard');
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/switch-tenant`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ tenantId: 'tenant-b' });
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      accessToken,
      tenant: {
        tenantId: 'tenant-b',
        subdomain: 'bravo',
        name: 'Bravo Payroll',
        status: 'trial',
      },
      redirectUrl: 'https://bravo.yourhrm.com/dashboard',
    });

    expect(service.getAccessToken()).toBe(accessToken);
    expect(service.currentTenant()?.tenantId).toBe('tenant-b');
    expect(service.roles()).toEqual(['Auditor']);
    expect(service.permissions()).toEqual(['Payroll.View']);
    expect(tenantService.setTenantFromAuth).toHaveBeenCalledWith(
      jasmine.objectContaining({ tenantId: 'tenant-b' })
    );
    expect(redirectSpy).toHaveBeenCalledWith('https://bravo.yourhrm.com/dashboard');
  });
});

function tokenFor(overrides: Partial<ITokenClaims>): string {
  const claims: ITokenClaims = {
    sub: 'user-1',
    email: 'auditor@example.com',
    tenant_id: 'tenant-a',
    user_tenant_id: 'membership-1',
    roles: ['Tenant Admin'],
    permissions: ['Admin.View'],
    is_impersonation: false,
    iat: 1,
    exp: 9999999999,
    iss: 'hris',
    aud: 'hris',
    ...overrides,
  };

  return `header.${btoa(JSON.stringify(claims))}.signature`;
}
