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

/** Tenant-level authentication settings */
export interface ITenantAuthSettings {
  mfaPolicy: 'off' | 'optional' | 'required';
  mfaRequiredRoles: string[];
}

/** MFA enrollment step for the wizard UI */
export type MfaEnrollStep = 'qr' | 'verify' | 'recovery';
