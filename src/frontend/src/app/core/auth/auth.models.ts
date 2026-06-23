/** Login request payload */
export interface ILoginRequest {
  email: string;
  password: string;
  mfaCode?: string;
}

/** Successful login response from the API */
export interface ILoginResponse {
  accessToken: string;
  user: IUser;
  tenant: ITenantInfo;
  permissions: string[];
  mfaChallenge?: boolean;
  mfaMethod?: 'totp';
  mfaEnrollmentRequired?: boolean;
}

/** Authenticated user profile */
export interface IUser {
  userId: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  mfaEnabled: boolean;
}

/** JWT token claims decoded from access token */
export interface ITokenClaims {
  sub: string;
  email: string;
  tenant_id: string;
  user_tenant_id: string;
  roles: string[];
  permissions: string[];
  is_impersonation: boolean;
  // ─── Impersonation claims (US-ADM-003 FR-2) ────────────────
  // Present only when is_impersonation is true. Decoded as-is from the JWT, so
  // string/number types match whatever the backend emits (imp_expires_at is a
  // unix-seconds value that may decode as a number or numeric string).
  /** Impersonation session id (the audit reference shown in the banner). */
  imp_session_id?: string;
  /** Original System Admin user_id who initiated the session. */
  imp_actor_id?: string;
  /** Truncated reason captured at session start. */
  imp_reason?: string;
  /** Read-only flag (AC-5/AC-6) — "true"/"false" string or boolean from the token. */
  imp_readonly?: boolean | string;
  /** Session hard-expiry as unix seconds (number or numeric string). */
  imp_expires_at?: number | string;
  iat: number;
  exp: number;
  iss: string;
  aud: string;
}

/** Tenant information returned with login */
export interface ITenantInfo {
  tenantId: string;
  subdomain: string;
  name: string;
  logoUrl?: string;
  primaryColor?: string;
  status: TenantStatus;
}

/** Tenant lifecycle status enum */
export type TenantStatus =
  | 'active'
  | 'trial'
  | 'past_due'
  | 'suspended'
  | 'terminating'
  | 'terminated';

/** Token refresh response */
export interface IRefreshResponse {
  accessToken: string;
}

/** Response of GET /auth/me — used to hydrate session state after an SSO redirect (CR-AUTH-001). */
export interface ICurrentUserResponse {
  userId: string;
  email: string;
  displayName: string;
  tenant: ITenantInfo;
  roles: string[];
  permissions: string[];
  mfaEnabled: boolean;
}

/** Forgot password request */
export interface IForgotPasswordRequest {
  email: string;
}

/** Reset password request */
export interface IResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

/** Generic API message response */
export interface IMessageResponse {
  message: string;
}

/** User's tenant membership (for tenant switcher) */
export interface IUserTenant {
  tenantId: string;
  subdomain: string;
  name: string;
  logoUrl?: string;
  status: TenantStatus;
  roles: string[];
  isCurrentTenant: boolean;
}

/** Switch tenant request */
export interface ISwitchTenantRequest {
  tenantId: string;
}

/** Switch tenant response */
export interface ISwitchTenantResponse {
  accessToken: string;
  tenant: ITenantInfo;
  redirectUrl: string;
}

/** Active session information */
export interface ISession {
  sessionId: string;
  device: string;
  browser: string;
  os: string;
  ipAddress: string;
  issuedAt: string;
  lastActiveAt: string;
  isCurrent: boolean;
}

/** Auth state for the application */
export interface IAuthState {
  user: IUser | null;
  tenant: ITenantInfo | null;
  permissions: string[];
  isAuthenticated: boolean;
  isLoading: boolean;
  mfaChallenge: boolean;
}

// ─── MFA (US-AUTH-005) ──────────────────────────────────────

/** Response from POST /auth/mfa/enroll */
export interface IMfaEnrollResponse {
  secret: string;
  qrCodeDataUrl: string;
  recoveryCodes: string[];
}

/** Request body for POST /auth/mfa/verify (enrollment verification) */
export interface IMfaVerifyRequest {
  code: string;
}

/** Response from POST /auth/mfa/verify */
export interface IMfaVerifyResponse {
  success: boolean;
  recoveryCodes?: string[];
}

/** Request body for POST /auth/mfa/challenge (login MFA step) */
export interface IMfaLoginVerifyRequest {
  code: string;
  email: string;
}

/** Tenant-level authentication settings (US-AUTH-005 + US-AUTH-009 + US-AUTH-010) */
export interface ITenantAuthSettings {
  mfaPolicy: 'off' | 'optional' | 'required';
  mfaRequiredRoles: string[];
  // Session policy fields (US-AUTH-009 FR-1) -- optional because
  // they are new additions and the backend provides defaults.
  idleTimeoutMinutes?: number;
  absoluteTimeoutHours?: number;
  maxConcurrentSessions?: number;
  concurrentSessionStrategy?: ConcurrentSessionStrategy;
  // Lockout policy fields (US-AUTH-010 FR-3) -- optional; backend provides defaults.
  maxFailedAttempts?: number;
  lockoutDurationMinutes?: number;
  progressiveLockoutEnabled?: boolean;
}

/** Concurrent session strategy (US-AUTH-009 FR-1) */
export type ConcurrentSessionStrategy = 'deny_new' | 'revoke_oldest';

/** Session policy update payload (subset for session-only updates) */
export interface ISessionPolicyUpdate {
  idleTimeoutMinutes: number;
  absoluteTimeoutHours: number;
  maxConcurrentSessions: number;
  concurrentSessionStrategy: ConcurrentSessionStrategy;
}

/** MFA enrollment step for the wizard UI */
export type MfaEnrollStep = 'qr' | 'verify' | 'recovery';

// ─── Account Lockout (US-AUTH-010) ─────────────────────────────

/** Lockout policy fields within tenant auth settings (US-AUTH-010 FR-3) */
export interface ILockoutPolicy {
  maxFailedAttempts: number;
  lockoutDurationMinutes: number;
  progressiveLockoutEnabled: boolean;
}

/**
 * Tenant user summary for admin user management lists.
 * Includes lockout state so admins can see locked accounts (US-AUTH-010 AC-5).
 */
export interface ITenantUser {
  userId: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  roles: string[];
  isActive: boolean;
  /** Null when not locked; ISO timestamp of lockout expiry when locked */
  lockedUntil: string | null;
  failedLoginCount: number;
  lastLoginAt: string | null;
}

/**
 * Error body shape the backend returns on a lockout 401.
 * The `code` field distinguishes a lockout from a generic credential failure.
 */
export interface ILoginErrorResponse {
  message: string;
  code?: 'account_locked' | 'invalid_credentials' | string;
  lockoutMinutesRemaining?: number;
}
