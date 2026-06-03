import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, BehaviorSubject, throwError, of } from 'rxjs';
import { tap, catchError, switchMap, finalize, filter, take } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { environment } from '../../../environments/environment';
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
} from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

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
          // Redirect to the new tenant subdomain
          window.location.href = response.redirectUrl;
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
      return;
    }

    this.mfaChallenge.set(false);
    this.setAccessToken(response.accessToken);
    this.currentUser.set(response.user);
    this.currentTenant.set(response.tenant);
    this.permissions.set(response.permissions);

    // Decode roles from the JWT claims
    const claims = this.decodeToken();
    if (claims?.roles) {
      this.roles.set(claims.roles);
    }
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
}
