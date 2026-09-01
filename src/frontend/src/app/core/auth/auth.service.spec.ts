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
import {
  ITokenClaims,
  // D1 slice 4: every fixture below is typed as the GENERATED wire alias, so a fixture that
  // carries a view-model-only key (e.g. a `status` on the tenant, which AuthTenantDto has never
  // had) is a compile error rather than a test that proves nothing.
  CurrentUserWire,
  UserTenantWire,
  SwitchTenantResponseWire,
  SessionWire,
  TenantUserPageWire,
  LoginResponseWire,
  MfaVerifyResponseWire,
  TenantAuthSettingsWire,
  MessageResponseWire,
} from './auth.models';

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
    // The backend serializes the TenantStatus ENUM as its PascalCase name
    // (JsonStringEnumConverter, Program.cs), never the lowercase snake_case the FE union uses.
    const wire: UserTenantWire[] = [
      {
        tenantId: 'tenant-a',
        subdomain: 'acme',
        name: 'Acme HR',
        status: 'Active',
        roles: ['Tenant Admin'],
        isCurrentTenant: true,
      },
      {
        tenantId: 'tenant-b',
        subdomain: 'bravo',
        name: 'Bravo Payroll',
        status: 'Trial',
        roles: ['Auditor'],
        isCurrentTenant: false,
      },
    ];

    service.getMyTenants().subscribe((tenants) => {
      expect(tenants.length).toBe(2);
      expect(tenants[0].name).toBe('Acme HR');
      // POSITIVE: the PascalCase wire value decodes to the FE union the switcher compares against
      // (`isTenantSwitchable` allow-lists 'active'/'trial'). Without the decode both are 'suspended'.
      expect(tenants[0].status).toBe('active');
      expect(tenants[1].status).toBe('trial');
      expect(tenants[0].isCurrentTenant).toBeTrue();
      expect(tenants[1].isCurrentTenant).toBeFalse();
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/my-tenants`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(wire);
  });

  it('decodes PastDue and falls back to suspended for an absent membership status', () => {
    const wire: UserTenantWire[] = [
      { tenantId: 'tenant-c', subdomain: 'charlie', name: 'Charlie Ltd', status: 'PastDue' },
      { tenantId: 'tenant-d', subdomain: 'delta', name: 'Delta Ltd' },
    ];

    service.getMyTenants().subscribe((tenants) => {
      // POSITIVE twin: a multi-word PascalCase status still resolves to a real union member.
      expect(tenants[0].status).toBe('past_due');
      // FAIL CLOSED: an absent status must never read as 'active' — the switcher allow-lists
      // 'active'/'trial', so 'suspended' blocks the switch instead of claiming a healthy tenant.
      expect(tenants[1].status).toBe('suspended');
      // Absent roles deny rather than throw (`roles[0] || 'Member'` in main-layout).
      expect(tenants[1].roles).toEqual([]);
      expect(tenants[1].isCurrentTenant).toBeFalse();
    });

    httpMock.expectOne(`${environment.apiBaseUrl}/auth/my-tenants`).flush(wire);
  });

  // ─── Session management (US-AUTH-009) ────────────────────

  it('fetches user own sessions with credentials', () => {
    const mockSessions: SessionWire[] = [
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
      // POSITIVE twin for the fail-closed arm below: these fields CAN carry a value.
      expect(sessions[0].isCurrent).toBeTrue();
      expect(sessions[0].browser).toBe('Chrome');
      expect(sessions[0].lastActiveAt).toBe('2026-06-01T01:00:00Z');
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/me/sessions`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockSessions);
  });

  it('marks a session as not-current when the wire omits the flag', () => {
    const wire: SessionWire[] = [{ sessionId: 's9' }];

    service.getMySessions().subscribe((sessions) => {
      // FAIL CLOSED: `false` leaves the Revoke button ENABLED. A wrong `true` would make a
      // hijacked session un-revocable (my-sessions disables the button on isCurrent).
      expect(sessions[0].isCurrent).toBeFalse();
      expect(sessions[0].lastActiveAt).toBe('');
      expect(sessions[0].browser).toBe('');
      expect(sessions[0].sessionId).toBe('s9');
    });

    httpMock.expectOne(`${environment.apiBaseUrl}/auth/me/sessions`).flush(wire);
  });

  it('yields an empty message for an ApiResponse envelope that carries none', () => {
    // The NON-generic ApiResponse has no `data` key, so apiEnvelopeInterceptor passes it through
    // whole, and `ApiResponse.Ok()` genuinely emits a null message. '' claims nothing;
    // `undefined` would surface as the string "undefined" in a toast.
    const wire: MessageResponseWire = { success: true, code: null, errors: null };

    service.revokeSession('s3').subscribe((resp) => {
      expect(resp.message).toBe('');
    });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/auth/me/sessions/s3/revoke`)
      .flush(wire);
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
      `${environment.apiBaseUrl}/tenant/users/by-user/user-abc/sessions`
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
      `${environment.apiBaseUrl}/tenant/users/by-user/user-abc/sessions/revoke`
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
      `${environment.apiBaseUrl}/tenant/users/by-user/user-abc/sessions/revoke`
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
      `${environment.apiBaseUrl}/tenant/users/by-user/user-locked/unlock`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ message: 'Account unlocked' });
  });

  it('fetches tenant users list with credentials', () => {
    // GET /tenant/users returns ApiResponse<PagedResult<TenantUserListItemDto>>. The envelope
    // interceptor unwraps only the OUTER { success, data } and deliberately leaves the paging
    // envelope alone, so what arrives here is the PagedResult -- NOT an array. `roles` are
    // objects on the wire; both callers render them as strings.
    const wire: TenantUserPageWire = {
      items: [
        {
          userId: 'user-1',
          userTenantId: 'membership-1',
          email: 'alice@acme.com',
          displayName: 'Alice Smith',
          status: 'Active',
          roles: [
            { roleId: 'role-1', name: 'Employee' },
            { roleId: 'role-2', name: 'Tenant Admin' },
          ],
          lastLoginAt: '2026-06-01T10:00:00Z',
          linkedEmployeeId: 'emp-1',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    };

    service.getTenantUsers().subscribe((users) => {
      expect(users.length).toBe(1);
      expect(users[0].email).toBe('alice@acme.com');
      // POSITIVE: the object->string role decode CAN produce values. Without the mapper these
      // are TenantUserRoleDto objects and `roles.join(', ')` renders "[object Object]".
      expect(users[0].roles).toEqual(['Employee', 'Tenant Admin']);
      // POSITIVE: 'Active' (the PascalCase enum name) is what makes a break-glass candidate.
      expect(users[0].isActive).toBeTrue();
      expect(users[0].lastLoginAt).toBe('2026-06-01T10:00:00Z');
    });

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users`
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(wire);
  });

  it('treats a non-Active tenant user as inactive and denies its roles when absent', () => {
    const wire: TenantUserPageWire = {
      items: [
        {
          userId: 'user-2',
          email: 'bob@acme.com',
          displayName: 'Bob Jones',
          status: 'Disabled',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    };

    service.getTenantUsers().subscribe((users) => {
      // FAIL CLOSED: anything that is not exactly 'Active' is excluded from the sso_only
      // break-glass admin candidate list (sso-settings filters on `u.isActive && u.roles...`).
      expect(users[0].isActive).toBeFalse();
      expect(users[0].roles).toEqual([]);
      // NO WIRE SOURCE: TenantUserListItemDto carries no lockout state at all.
      expect(users[0].lockedUntil).toBeNull();
      expect(users[0].failedLoginCount).toBeUndefined();
    });

    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/users`).flush(wire);
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
    // AuthSwitchTenantResponse.tenant is AuthTenantDto = { tenantId, subdomain, name } only.
    const wire: SwitchTenantResponseWire = {
      accessToken,
      tenant: {
        tenantId: 'tenant-b',
        subdomain: 'bravo',
        name: 'Bravo Payroll',
      },
      redirectUrl: 'https://bravo.yourhrm.com/dashboard',
    };
    req.flush(wire);

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
      const me: CurrentUserWire = {
        userId: 'user-9',
        email: 'restored@acme.com',
        displayName: 'Restored User',
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR' },
        roles: ['HR Officer'],
        permissions: ['Employee.View.All'],
        mfaEnabled: false,
      };
      meReq.flush(me);

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

  // ─── D1 slice 4: wire-contract mappers ──────────────────────
  // Every arm below pairs an ABSENCE assertion with a POSITIVE twin, so a mapper that was
  // deleted outright would fail rather than pass vacuously.
  describe('wire mappers', () => {
    it('hydrates the session from a fully populated login payload', () => {
      const accessToken = tokenFor({
        roles: ['Tenant Admin'],
        permissions: ['Users.Manage'],
      });
      // AuthUserDto is { userId, email, displayName } -- it has NO mfaEnabled and no avatarUrl.
      const wire: LoginResponseWire = {
        accessToken,
        user: { userId: 'user-1', email: 'alice@acme.com', displayName: 'Alice' },
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR' },
        permissions: ['Users.Manage'],
        mfaChallenge: false,
        refreshToken: 'REFRESH-MUST-NOT-REACH-THE-VIEW-MODEL',
      };

      let seen: unknown;
      service.login({ email: 'alice@acme.com', password: 'x' }).subscribe((r) => {
        seen = r;
      });

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(wire);

      // POSITIVE: the mapper genuinely produces every field the session depends on.
      expect(service.getAccessToken()).toBe(accessToken);
      expect(service.currentUser()?.email).toBe('alice@acme.com');
      expect(service.currentTenant()?.tenantId).toBe('tenant-a');
      expect(service.roles()).toEqual(['Tenant Admin']);
      expect(service.permissions()).toEqual(['Users.Manage']);
      // The refresh token lives in an httpOnly cookie by design; it must never be copied into a
      // JS-reachable view model even when the server sends one.
      expect(
        Object.prototype.hasOwnProperty.call(seen, 'refreshToken')
      ).toBeFalse();
      // NO WIRE SOURCE on the login DTO: claiming MFA is on would tell an unprotected user they
      // are protected. Only /auth/me is authoritative (see the twin below).
      expect(service.mfaEnabled()).toBeFalse();
    });

    it('denies everything when the login payload omits the token and permissions', () => {
      const wire: LoginResponseWire = {
        user: { userId: 'user-1', email: 'alice@acme.com' },
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR' },
        permissions: null,
      };

      service.login({ email: 'alice@acme.com', password: 'x' }).subscribe((r) => {
        expect(r.permissions).toEqual([]);
        expect(r.accessToken).toBe('');
      });

      httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`).flush(wire);

      // FAIL CLOSED: '' means authInterceptor attaches no Authorization header, so the session is
      // unauthenticated rather than unauthorized, and no permission is granted.
      expect(service.getAccessToken()).toBe('');
      expect(service.permissions()).toEqual([]);
      expect(service.hasPermission('Users.Manage')).toBeFalse();
      expect(service.hasAnyPermission(['Users.Manage'])).toBeFalse();
    });

    it('grants no roles or permissions when /auth/me omits them', async () => {
      const accessToken = tokenFor({});
      const restore = service.restoreSession();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/auth/refresh`)
        .flush({ accessToken });

      // roles/permissions are `string[] | null` on the wire. Absent must mean "no authority",
      // never "unrestricted". The positive twin is the restoreSession test above, which proves
      // the same path DOES populate ['HR Officer'] / ['Employee.View.All'] when the wire has them.
      const me: CurrentUserWire = {
        userId: 'user-9',
        email: 'restored@acme.com',
        displayName: 'Restored User',
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR' },
        roles: null,
        permissions: null,
        mfaEnabled: true,
      };
      httpMock.expectOne(`${environment.apiBaseUrl}/auth/me`).flush(me);
      await restore;

      // Not vacuous: hydration definitely ran (the session is authenticated and the user is set),
      // it simply granted nothing.
      expect(service.isAuthenticated()).toBeTrue();
      expect(service.currentUser()?.email).toBe('restored@acme.com');
      expect(service.roles()).toEqual([]);
      expect(service.permissions()).toEqual([]);
      expect(service.hasAllPermissions(['Employee.View.All'])).toBeFalse();
      expect(service.hasAnyPermission(['Employee.View.All'])).toBeFalse();
      // POSITIVE twin for the login arm: mfaEnabled DOES have a wire source on /auth/me.
      expect(service.mfaEnabled()).toBeTrue();
    });

    it('never invents a tenant status from the auth payload', async () => {
      const restore = service.restoreSession();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/auth/refresh`)
        .flush({ accessToken: tokenFor({}) });

      const me: CurrentUserWire = {
        userId: 'user-9',
        email: 'restored@acme.com',
        displayName: 'Restored User',
        tenant: { tenantId: 'tenant-a', subdomain: 'acme', name: 'Acme HR' },
        roles: ['HR Officer'],
        permissions: [],
        mfaEnabled: false,
      };
      httpMock.expectOne(`${environment.apiBaseUrl}/auth/me`).flush(me);
      await restore;

      const forwarded = tenantService.setTenantFromAuth.calls.mostRecent().args[0];
      // POSITIVE: the mapper ran and produced the fields the wire DOES carry.
      expect(forwarded.tenantId).toBe('tenant-a');
      expect(forwarded.subdomain).toBe('acme');
      expect(forwarded.name).toBe('Acme HR');
      // AuthTenantDto carries no status. It must stay ABSENT: setTenantFromAuth resolves
      // `tenant.status ?? previousContext.status ?? 'active'`, so an invented 'active' here would
      // overwrite a `suspended` status already resolved from /tenant/context and silently
      // un-suspend the tenant for tenantGuard.
      expect(forwarded.status).toBeUndefined();
      expect(service.currentTenant()?.status).toBeUndefined();
    });

    it('marks MFA enrollment complete only when the server confirms it', () => {
      service.currentUser.set({
        userId: 'user-1',
        email: 'alice@acme.com',
        displayName: 'Alice',
        mfaEnabled: false,
      });
      service.mfaRequiresEnrollment.set(true);
      const wire: MfaVerifyResponseWire = {
        success: true,
        recoveryCodes: ['code-1', 'code-2'],
      };

      service.verifyMfaEnrollment('123456').subscribe((r) => {
        expect(r.success).toBeTrue();
        expect(r.recoveryCodes).toEqual(['code-1', 'code-2']);
      });

      httpMock.expectOne(`${environment.apiBaseUrl}/auth/mfa/verify`).flush(wire);

      // POSITIVE: a confirmed verification DOES flip the local state.
      expect(service.currentUser()?.mfaEnabled).toBeTrue();
      expect(service.mfaRequiresEnrollment()).toBeFalse();
    });

    it('does not mark MFA as enrolled when the verify payload omits success', () => {
      service.currentUser.set({
        userId: 'user-1',
        email: 'alice@acme.com',
        displayName: 'Alice',
        mfaEnabled: false,
      });
      service.mfaRequiresEnrollment.set(true);
      const wire: MfaVerifyResponseWire = { recoveryCodes: null };

      service.verifyMfaEnrollment('123456').subscribe((r) => {
        // FAIL CLOSED: defaulting `success` to true would mark an account MFA-protected without
        // the server ever confirming the code.
        expect(r.success).toBeFalse();
        expect(r.recoveryCodes).toBeUndefined();
      });

      httpMock.expectOne(`${environment.apiBaseUrl}/auth/mfa/verify`).flush(wire);

      expect(service.currentUser()?.mfaEnabled).toBeFalse();
      expect(service.mfaRequiresEnrollment()).toBeTrue();
    });

    it('passes tenant auth settings through without inventing values', () => {
      const wire: TenantAuthSettingsWire = {
        mfaPolicy: 'optional',
        mfaRequiredRoles: ['Tenant Admin'],
        idleTimeoutMinutes: 15,
        concurrentSessionStrategy: 'revoke_oldest',
        enforcementMode: 'sso_only',
        ssoOnboardingStatus: 'consented',
        ssoEntitled: true,
      };

      service.getTenantAuthSettings().subscribe((settings) => {
        // POSITIVE: every narrowed union resolves to the real server value.
        expect(settings.mfaPolicy).toBe('optional');
        expect(settings.mfaRequiredRoles).toEqual(['Tenant Admin']);
        expect(settings.idleTimeoutMinutes).toBe(15);
        expect(settings.concurrentSessionStrategy).toBe('revoke_oldest');
        expect(settings.enforcementMode).toBe('sso_only');
        expect(settings.ssoOnboardingStatus).toBe('consented');
        expect(settings.ssoEntitled).toBeTrue();
      });

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/tenant/auth-settings`
      );
      expect(req.request.withCredentials).toBeTrue();
      req.flush(wire);
    });

    it('never downgrades an unreadable MFA policy to off, and keeps entitlement absent', () => {
      const wire: TenantAuthSettingsWire = {};

      service.getTenantAuthSettings().subscribe((settings) => {
        // mfaPolicy is read-modify-WRITTEN by three settings screens, and the backend request DTO
        // has a NON-nullable MfaPolicy defaulting to "off" -- so both 'off' and undefined would
        // silently disable tenant MFA enforcement on the next save. 'required' is the only
        // non-permissive answer left.
        expect(settings.mfaPolicy).toBe('required');
        expect(settings.mfaRequiredRoles).toEqual([]);
        // The remaining unions stay ABSENT so each read site keeps applying its own restrictive
        // fallback, and a PUT omits them (nullable on the request = "leave unchanged").
        expect(settings.concurrentSessionStrategy).toBeUndefined();
        expect(settings.enforcementMode).toBeUndefined();
        expect(settings.ssoOnboardingStatus).toBeUndefined();
        expect(settings.idleTimeoutMinutes).toBeUndefined();
        // The SSO card gates on `ssoEntitled === true`; an absent flag must stay absent.
        expect(settings.ssoEntitled).toBeUndefined();
      });

      httpMock
        .expectOne(`${environment.apiBaseUrl}/tenant/auth-settings`)
        .flush(wire);
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
