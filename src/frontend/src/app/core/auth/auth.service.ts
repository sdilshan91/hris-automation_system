import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, BehaviorSubject, throwError, of } from 'rxjs';
import { tap, catchError, switchMap, finalize, filter, take } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { environment } from '../../../environments/environment';
import { TenantService } from '../tenant/tenant.service';
import {
  ILoginRequest,
  ILoginResponse,
  IRefreshResponse,
  IForgotPasswordRequest,
  IResetPasswordRequest,
  IMessageResponse,
  IUser,
  ITenantInfo,
  ITokenClaims,
  IUserTenant,
  ISwitchTenantRequest,
  ISwitchTenantResponse,
  ISession,
  IMfaEnrollResponse,
  IMfaVerifyResponse,
  ITenantAuthSettings,
  ITenantUser,
} from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);
  private readonly tenantService = inject(TenantService);

  private readonly apiUrl = environment.apiBaseUrl;

  // In-memory only -- never stored in localStorage (XSS protection)
  private accessToken: string | null = null;

  // Signals for reactive state
  readonly currentUser = signal<IUser | null>(null);
  readonly currentTenant = signal<ITenantInfo | null>(null);
  readonly permissions = signal<string[]>([]);
  readonly roles = signal<string[]>([]);
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  readonly isLoading = signal(false);
  readonly mfaChallenge = signal(false);

  // MFA state (US-AUTH-005)
  readonly mfaEnabled = computed(() => this.currentUser()?.mfaEnabled ?? false);
  readonly mfaRequiresEnrollment = signal(false);
  readonly loginEmail = signal('');

  // Refresh token queue management (FR-10 from US-AUTH-002)
  private isRefreshing = false;
  private refreshSubject$ = new BehaviorSubject<string | null>(null);

  // ─── Login ───────────────────────────────────────────────

  login(request: ILoginRequest): Observable<ILoginResponse> {
    this.isLoading.set(true);
    return this.http
      .post<ILoginResponse>(`${this.apiUrl}/auth/login`, request, {
        withCredentials: true, // include httpOnly cookies
      })
      .pipe(
        tap((response) => this.handleLoginResponse(response)),
        catchError((error) => {
          this.isLoading.set(false);
          return throwError(() => error);
        }),
        finalize(() => this.isLoading.set(false))
      );
  }

  // ─── Logout ──────────────────────────────────────────────

  logout(): void {
    this.http
      .post<IMessageResponse>(`${this.apiUrl}/auth/logout`, null, {
        withCredentials: true,
      })
      .pipe(
        catchError(() => {
          // AC-5 of US-AUTH-003: clear local state even if API call fails
          return of(null);
        })
      )
      .subscribe(() => {
        this.clearSession();
        this.router.navigate(['/auth/login']);
        this.toastr.info('You have been logged out.');
      });
  }

  // ─── Token Refresh ───────────────────────────────────────

  refreshToken(): Observable<string> {
    if (this.isRefreshing) {
      // Queue concurrent requests -- wait for the single refresh to complete
      return this.refreshSubject$.pipe(
        filter((token): token is string => token !== null),
        take(1)
      );
    }

    this.isRefreshing = true;
    this.refreshSubject$.next(null);

    return this.http
      .post<IRefreshResponse>(`${this.apiUrl}/auth/refresh`, null, {
        withCredentials: true,
      })
      .pipe(
        tap((response) => {
          this.setAccessToken(response.accessToken);
          this.refreshSubject$.next(response.accessToken);
        }),
        switchMap((response) => of(response.accessToken)),
        catchError((error) => {
          this.clearSession();
          this.router.navigate(['/auth/login']);
          this.toastr.warning('Session expired. Please log in again.');
          return throwError(() => error);
        }),
        finalize(() => {
          this.isRefreshing = false;
        })
      );
  }

  // ─── Forgot Password ────────────────────────────────────

  forgotPassword(request: IForgotPasswordRequest): Observable<IMessageResponse> {
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/auth/forgot-password`,
      request
    );
  }

  // ─── Reset Password ─────────────────────────────────────

  resetPassword(request: IResetPasswordRequest): Observable<IMessageResponse> {
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/auth/reset-password`,
      request
    );
  }

  // ─── My Tenants (for tenant switcher) ────────────────────

  getMyTenants(): Observable<IUserTenant[]> {
    return this.http.get<IUserTenant[]>(`${this.apiUrl}/auth/my-tenants`, {
      withCredentials: true,
    });
  }

  // ─── Switch Tenant ───────────────────────────────────────

  switchTenant(request: ISwitchTenantRequest): Observable<ISwitchTenantResponse> {
    return this.http
      .post<ISwitchTenantResponse>(`${this.apiUrl}/auth/switch-tenant`, request, {
        withCredentials: true,
      })
      .pipe(
        tap((response) => {
          this.handleTenantSwitchResponse(response);
          this.redirectTo(response.redirectUrl);
        })
      );
  }

  // ─── Sessions ────────────────────────────────────────────

  getMySessions(): Observable<ISession[]> {
    return this.http.get<ISession[]>(`${this.apiUrl}/auth/me/sessions`, {
      withCredentials: true,
    });
  }

  revokeSession(sessionId: string): Observable<IMessageResponse> {
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/auth/me/sessions/${sessionId}/revoke`,
      null,
      { withCredentials: true }
    );
  }

  // ─── Admin Session Management (US-AUTH-009) ─────────────

  getUserSessions(userId: string): Observable<ISession[]> {
    return this.http.get<ISession[]>(
      `${this.apiUrl}/tenant/users/${userId}/sessions`,
      { withCredentials: true }
    );
  }

  revokeUserSession(userId: string, sessionId?: string): Observable<IMessageResponse> {
    const body = sessionId ? { sessionId } : {};
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/tenant/users/${userId}/sessions/revoke`,
      body,
      { withCredentials: true }
    );
  }

  // ─── Session Keep-Alive (US-AUTH-009 BR-6) ──────────────

  keepAlive(): Observable<IMessageResponse> {
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/auth/me/keep-alive`,
      null,
      { withCredentials: true }
    );
  }

  // ─── MFA Enrollment (US-AUTH-005) ───────────────────────

  enrollMfa(): Observable<IMfaEnrollResponse> {
    return this.http.post<IMfaEnrollResponse>(
      `${this.apiUrl}/auth/mfa/enroll`,
      null,
      { withCredentials: true }
    );
  }

  verifyMfaEnrollment(code: string): Observable<IMfaVerifyResponse> {
    return this.http
      .post<IMfaVerifyResponse>(
        `${this.apiUrl}/auth/mfa/verify`,
        { code },
        { withCredentials: true }
      )
      .pipe(
        tap((response) => {
          if (response.success) {
            // Update local user state to reflect MFA is now enabled
            const user = this.currentUser();
            if (user) {
              this.currentUser.set({ ...user, mfaEnabled: true });
            }
            this.mfaRequiresEnrollment.set(false);
          }
        })
      );
  }

  verifyMfaLogin(email: string, code: string): Observable<ILoginResponse> {
    return this.http
      .post<ILoginResponse>(
        `${this.apiUrl}/auth/mfa/challenge`,
        { email, code },
        { withCredentials: true }
      )
      .pipe(
        tap((response) => this.handleLoginResponse(response))
      );
  }

  disableMfa(): Observable<void> {
    return this.http
      .delete<void>(`${this.apiUrl}/auth/mfa`, {
        withCredentials: true,
      })
      .pipe(
        tap(() => {
          const user = this.currentUser();
          if (user) {
            this.currentUser.set({ ...user, mfaEnabled: false });
          }
        })
      );
  }

  getTenantAuthSettings(): Observable<ITenantAuthSettings> {
    return this.http.get<ITenantAuthSettings>(
      `${this.apiUrl}/tenant/auth-settings`,
      { withCredentials: true }
    );
  }

  updateTenantAuthSettings(settings: ITenantAuthSettings): Observable<void> {
    return this.http.put<void>(
      `${this.apiUrl}/tenant/auth-settings`,
      settings,
      { withCredentials: true }
    );
  }

  /** Cancel an in-progress MFA challenge and return to login */
  cancelMfaChallenge(): void {
    this.clearSession();
  }

  // ─── Account Lockout (US-AUTH-010) ─────────────────────────

  /**
   * Admin unlock of a locked user account (US-AUTH-010 AC-5).
   * POST /tenant/users/{userId}/unlock
   */
  unlockUser(userId: string): Observable<IMessageResponse> {
    return this.http.post<IMessageResponse>(
      `${this.apiUrl}/tenant/users/${userId}/unlock`,
      null,
      { withCredentials: true }
    );
  }

  /**
   * Get tenant users list for admin user management (US-AUTH-010 FR-6).
   * GET /tenant/users
   */
  getTenantUsers(): Observable<ITenantUser[]> {
    return this.http.get<ITenantUser[]>(
      `${this.apiUrl}/tenant/users`,
      { withCredentials: true }
    );
  }

  // ─── Token Access ────────────────────────────────────────

  getAccessToken(): string | null {
    return this.accessToken;
  }

  /** Check if user has a specific permission */
  hasPermission(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  /** Check if user has any of the given permissions */
  hasAnyPermission(perms: string[]): boolean {
    const current = this.permissions();
    return perms.some((p) => current.includes(p));
  }

  /** Check if user has a specific role (decoded from JWT) */
  hasRole(role: string): boolean {
    const claims = this.decodeToken();
    return claims?.roles?.includes(role) ?? false;
  }

  /** Check if token is expired */
  isTokenExpired(): boolean {
    const claims = this.decodeToken();
    if (!claims) return true;
    const now = Math.floor(Date.now() / 1000);
    return claims.exp <= now;
  }

  /** Check if token needs refresh (within buffer window) */
  shouldRefreshToken(): boolean {
    const claims = this.decodeToken();
    if (!claims) return false;
    const now = Math.floor(Date.now() / 1000);
    return claims.exp - now <= environment.tokenRefreshBufferSeconds;
  }

  // ─── Private Helpers ─────────────────────────────────────

  /** Check if user has all of the given permissions */
  hasAllPermissions(perms: string[]): boolean {
    const current = this.permissions();
    return perms.every((p) => current.includes(p));
  }

  private handleLoginResponse(response: ILoginResponse): void {
    if (response.mfaChallenge) {
      this.mfaChallenge.set(true);
      // Use explicit backend flag; fall back to proxy for backwards compatibility
      if (response.mfaEnrollmentRequired ?? (response.user && !response.user.mfaEnabled)) {
        this.mfaRequiresEnrollment.set(true);
      } else {
        this.mfaRequiresEnrollment.set(false);
      }
      return;
    }

    this.mfaChallenge.set(false);
    this.mfaRequiresEnrollment.set(false);
    this.setAccessToken(response.accessToken);
    this.currentUser.set(response.user);
    this.currentTenant.set(response.tenant);
    this.tenantService.setTenantFromAuth(response.tenant);
    this.permissions.set(response.permissions);

    // Decode roles from the JWT claims
    const claims = this.decodeToken();
    this.roles.set(claims?.roles ?? []);
    this.permissions.set(claims?.permissions ?? response.permissions);
  }

  private handleTenantSwitchResponse(response: ISwitchTenantResponse): void {
    this.setAccessToken(response.accessToken);
    this.currentTenant.set(response.tenant);
    this.tenantService.setTenantFromAuth(response.tenant);

    const claims = this.decodeToken();
    this.roles.set(claims?.roles ?? []);
    this.permissions.set(claims?.permissions ?? []);
  }

  private setAccessToken(token: string): void {
    this.accessToken = token;
  }

  private clearSession(): void {
    this.accessToken = null;
    this.currentUser.set(null);
    this.currentTenant.set(null);
    this.permissions.set([]);
    this.roles.set([]);
    this.mfaChallenge.set(false);
    this.mfaRequiresEnrollment.set(false);
    this.loginEmail.set('');
  }

  private decodeToken(): ITokenClaims | null {
    if (!this.accessToken) return null;
    try {
      const payload = this.accessToken.split('.')[1];
      const decoded = atob(payload);
      return JSON.parse(decoded) as ITokenClaims;
    } catch {
      return null;
    }
  }

  private redirectTo(url: string): void {
    window.location.href = url;
  }
}
