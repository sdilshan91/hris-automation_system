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

  // ─── Account Lockout (US-AUTH-010) ──────────────────────

  it('unlocks a user account with credentials', () => {
    service.unlockUser('user-locked').subscribe((resp) => {
      expect(resp.message).toBe('Account unlocked');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users/user-locked/unlock`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ message: 'Account unlocked' });
  });

  it('fetches tenant users list with credentials', () => {
    service.getTenantUsers().subscribe((users) => {
      expect(users.length).toBe(1);
      expect(users[0].email).toBe('alice@acme.com');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users`
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([
      {
        userId: 'user-1',
        email: 'alice@acme.com',
        displayName: 'Alice Smith',
        roles: ['Employee'],
        isActive: true,
        lockedUntil: null,
        failedLoginCount: 0,
        lastLoginAt: '2026-06-01T10:00:00Z',
      },
    ]);
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

  // ─── Impersonation (US-ADM-003) ─────────────────────────────
  describe('impersonation', () => {
    it('isImpersonating is false for a normal token', () => {
      service.activateImpersonation(tokenFor({ is_impersonation: false }));
      // activating a non-impersonation token still exposes its claims; the flag
      // is what the banner reads.
      expect(service.isImpersonating()).toBeFalse();
    });

    it('exposes the impersonation claims from the active token', () => {
      service.activateImpersonation(
        tokenFor({
          is_impersonation: true,
          imp_session_id: 'sess-7',
          imp_readonly: 'true',
          imp_expires_at: 1800000000,
          roles: ['HR Officer'],
        }),
      );

      expect(service.isImpersonating()).toBeTrue();
      expect(service.impersonationSessionId()).toBe('sess-7');
      expect(service.impersonationReadOnly()).toBeTrue();
      expect(service.impersonationExpiresAt()).toBe(1800000000);
      // Roles re-derive from the impersonation JWT.
      expect(service.roles()).toEqual(['HR Officer']);
    });

    it('coerces a numeric-string imp_expires_at and boolean imp_readonly', () => {
      service.activateImpersonation(
        tokenFor({
          is_impersonation: true,
          imp_session_id: 'sess-8',
          imp_readonly: false,
          imp_expires_at: '1700000000',
        }),
      );

      expect(service.impersonationReadOnly()).toBeFalse();
      expect(service.impersonationExpiresAt()).toBe(1700000000);
    });

    it('endImpersonation restores the stashed admin token and clears impersonation', () => {
      const adminToken = tokenFor({ is_impersonation: false, roles: ['SystemAdmin'] });
      // Establish the admin session as the active token first.
      service.activateImpersonation(adminToken);
      // Now activate an impersonation token (admin token gets stashed).
      service.activateImpersonation(
        tokenFor({ is_impersonation: true, imp_session_id: 'sess-1' }),
      );
      expect(service.isImpersonating()).toBeTrue();

      const restored = service.endImpersonation();
      expect(restored).toBeTrue();
      expect(service.isImpersonating()).toBeFalse();
      expect(service.getAccessToken()).toBe(adminToken);
      expect(service.roles()).toEqual(['SystemAdmin']);
    });

    it('endImpersonation returns false when there is no stashed admin token', () => {
      service.activateImpersonation(
        tokenFor({ is_impersonation: true, imp_session_id: 'sess-1' }),
      );
      expect(service.endImpersonation()).toBeFalse();
    });
  });

  // ─── Bootstrap session restore (BUG-097) ─────────────────
  describe('restoreSession', () => {
    it('mints a token from the refresh cookie then hydrates the session from /auth/me', async () => {
      const accessToken = tokenFor({ roles: ['HR Officer'] });
      const restore = service.restoreSession();

      const refreshReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/auth/refresh`
      );
      expect(refreshReq.request.method).toBe('POST');
      expect(refreshReq.request.withCredentials).toBeTrue();
      refreshReq.flush({ accessToken });

      const meReq = httpMock.expectOne(`${environment.apiBaseUrl}/auth/me`);
      expect(meReq.request.method).toBe('GET');
      expect(meReq.request.withCredentials).toBeTrue();
      meReq.flush({
        userId: 'user-9',
        email: 'restored@acme.com',
        displayName: 'Restored User',
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR', status: 'active' },
        roles: ['HR Officer'],
        permissions: ['Employee.View.All'],
        mfaEnabled: false,
      });

      await restore;

      expect(service.getAccessToken()).toBe(accessToken);
      expect(service.isAuthenticated()).toBeTrue();
      expect(service.currentUser()?.email).toBe('restored@acme.com');
      expect(service.roles()).toEqual(['HR Officer']);
      expect(service.permissions()).toEqual(['Employee.View.All']);
      expect(tenantService.setTenantFromAuth).toHaveBeenCalledWith(
        jasmine.objectContaining({ tenantId: 'tenant-a' })
      );
    });

    it('resolves silently on a 401 (no refresh cookie) without navigating or toasting', async () => {
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
      const toastr = TestBed.inject(ToastrService) as jasmine.SpyObj<ToastrService>;

      const restore = service.restoreSession();

      const refreshReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/auth/refresh`
      );
      refreshReq.flush(
        { error: 'unauthorized' },
        { status: 401, statusText: 'Unauthorized' }
      );

      // No /auth/me is attempted when the refresh fails.
      httpMock.expectNone(`${environment.apiBaseUrl}/auth/me`);

      // The promise still resolves — bootstrap must not be blocked.
      await expectAsync(restore).toBeResolved();

      expect(service.getAccessToken()).toBeNull();
      expect(service.isAuthenticated()).toBeFalse();
      expect(service.currentUser()).toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
      expect(toastr.warning).not.toHaveBeenCalled();
    });
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
