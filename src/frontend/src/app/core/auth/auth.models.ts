import type { Schema } from '@core/api';

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
  /**
   * DF-27(b): recovery codes left after a recovery-code login. Present when the
   * login consumed a recovery code so the UI can warn when the pool runs low.
   */
  recoveryCodesRemaining?: number;
  /**
   * DF-27(b): backend hint that the user should regenerate their recovery codes
   * (e.g. the pool is nearly exhausted). Drives the regenerate prompt.
   */
  shouldRegenerateRecoveryCodes?: boolean;
}

/** Authenticated self-service change-password request (DF-27(c), US-AUTH-004). */
export interface IChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
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
  /**
   * D1 slice 4: OPTIONAL, because the auth wire does not carry it. `AuthTenantDto`
   * (login / switch-tenant / the nested tenant on /auth/me) is `{ tenantId, subdomain, name }`
   * and nothing else — no status, no logoUrl, no primaryColor.
   *
   * It MUST stay optional. `TenantService.setTenantFromAuth` resolves
   * `status: tenant.status ?? previousContext.status ?? 'active'`, so an invented `'active'`
   * here would OVERWRITE a `suspended` status already resolved from `/tenant/context` and
   * silently un-suspend the tenant for the `tenantGuard`. Absent must stay absent.
   */
  status?: TenantStatus;
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

/**
 * Reset password request. BUG-295: token-only — the reset token is a 256-bit secret bound to exactly one
 * user, so it identifies them on its own. The emailed link never carried an email address, which is why
 * requiring one here made every real reset link unusable.
 */
export interface IResetPasswordRequest {
  token: string;
  newPassword: string;
}

/**
 * BUG-294: invitation redemption. The invitee has no session; the one-time token from the emailed link
 * identifies both the invitation and the tenant it belongs to.
 */
export interface IAcceptInvitationRequest {
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

/** SSO enforcement mode (US-AUTH-012 FR-1). */
export type SsoEnforcementMode = 'optional' | 'sso_only';

/**
 * SSO admin-consent onboarding lifecycle (US-AUTH-016 FR-5/FR-6, data req §7).
 * `not_started` → `consent_pending` (admin-consent URL opened) → `consented`
 * (Entra Directory ID captured) → `enabled` (tenant admin explicitly turned SSO on).
 */
export type SsoOnboardingStatus =
  | 'not_started'
  | 'consent_pending'
  | 'consented'
  | 'enabled';

/** Tenant-level authentication settings (US-AUTH-005 + US-AUTH-009 + US-AUTH-010 + US-AUTH-012) */
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
  // SSO / Microsoft Entra ID config (US-AUTH-012 FR-1) -- optional; backend provides defaults
  // (SSO disabled by default, BR-1). Naming mirrors the existing camelCase DTO convention;
  // the snake_case in US-AUTH-012 section 7 is the DB/column naming.
  ssoEnabled?: boolean;
  allowedEntraTenantIds?: string[];
  allowedEmailDomains?: string[];
  jitEnabled?: boolean;
  jitDefaultRole?: string | null;
  enforcementMode?: SsoEnforcementMode;
  /**
   * US-AUTH-016 FR-2/FR-3 (BR-1): designated break-glass admin user IDs. At least
   * one is mandatory before `sso_only` can be enabled so a tenant can never lock
   * itself out. Optional on the wire — backend provides defaults / [] when absent.
   */
  breakGlassAdminUserIds?: string[];
  /**
   * US-AUTH-016 FR-5/FR-6: admin-consent onboarding lifecycle state. Optional;
   * treated as `not_started` when absent.
   */
  ssoOnboardingStatus?: SsoOnboardingStatus;
  /**
   * Read-only entitlement flag surfaced by GET (US-AUTH-012 AC-2, US-ADM-009
   * PlanFeatureFlags.Sso). When false/absent the SSO card is shown disabled with an
   * "Available on higher plans" badge (fail-closed). Never sent on write.
   */
  ssoEntitled?: boolean;
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
  /**
   * Null when not locked; ISO timestamp of lockout expiry when locked.
   * D1 slice 4: NO WIRE SOURCE — `UsersTenantUserListItemDto` carries no lockout state, so the
   * mapper can only ever emit `null` and `isLocked()` is permanently false. See the report.
   */
  lockedUntil: string | null;
  /**
   * D1 slice 4: OPTIONAL — no wire source either. Emitting `0` would be a fresh false claim
   * ("this account has had zero failed attempts"); `undefined` renders blank, which is what
   * the screen already shows today.
   */
  failedLoginCount?: number;
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

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// D1 slice 4 — WIRE CONTRACT → VIEW-MODEL MAPPERS  (core/auth)
//
// Before this slice every AuthService HTTP call named a hand-written `I…` type. `http.get<IFoo>()`
// is a CAST, not a check: TypeScript accepted whatever the server actually sent, and a shape
// mismatch surfaced as a silent `undefined` on screen. This is the AUTHORIZATION surface, so the
// field-by-field comparison against the generated contract found the most in any slice so far.
//
// Each `…Wire` alias below is a `Schema<'…'>` generated from the API's own OpenAPI document, so a
// backend DTO rename is now a compile error here instead of a runtime blank.
//
// `apiEnvelopeInterceptor` already unwraps `{ success, data }`, so these alias the INNER dto.
// The NON-generic `ApiResponse` has no `data` property at all (HRM.Application/DTOs/ApiResponse.cs),
// so it is passed through by the interceptor untouched — hence `MessageResponseWire`.
//
// Every generated property is optional (Swashbuckle emits no `required`), so every field is
// defaulted deliberately. THE GOVERNING RULE ON THIS SURFACE: a default may never grant, and may
// never assert that a protection is in place. Absent roles/permissions are `[]` (deny), an absent
// token is `''` (the interceptor then sends no Authorization header → 401), an absent
// `success`/`isCurrent`/`isActive` is `false`.
//
// ── FIELDS WITH NO WIRE SOURCE AT ALL (emitted as the least-claiming value, all flagged) ───────
//   IUser.mfaEnabled          `AuthUserDto` is {userId, email, displayName}. Hardcoded `false`:
//                             claims MFA is NOT set up, so the UI offers enrollment. `true` would
//                             tell an unprotected user they are protected. Only /auth/me is
//                             authoritative (`AuthCurrentUserDto.mfaEnabled`).
//   IUser.avatarUrl           omitted.
//   ITenantInfo.status        left `undefined` — see the banner on ITenantInfo. Inventing
//   ITenantInfo.logoUrl       `'active'` would un-suspend a suspended tenant.
//   ITenantInfo.primaryColor  omitted (branding comes from /tenant/context).
//   ITenantUser.lockedUntil   `null`; `UsersTenantUserListItemDto` has no lockout state.
//   ITenantUser.failedLoginCount / avatarUrl   omitted.
//
// ── WIRE FIELDS DELIBERATELY NOT MAPPED ───────────────────────────────────────────────────────
//   AuthLoginResponse.refreshToken / AuthSwitchTenantResponse.refreshToken
//       NEVER mapped. The refresh token lives in an httpOnly cookie by design; copying it into a
//       JS-reachable view model would widen the XSS blast radius. (The backend already nulls it at
//       AuthController.cs:77 — this is the second lock, not the first.)
//   AuthCurrentUserDto.tenantMemberships
//       Real, populated, and not declared on ICurrentUserResponse. Left unmapped — inventing UI for
//       it is out of lane. `getMyTenants()` already serves the switcher from the same DTO.
//   UsersTenantUserListItemDto.userTenantId / linkedEmployeeId / status(raw)
//       No FE home; `status` is consumed only to derive `isActive`.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

export type LoginResponseWire = Schema<'AuthLoginResponse'>;
export type UserWire = Schema<'AuthUserDto'>;
export type TenantInfoWire = Schema<'AuthTenantDto'>;
export type RefreshResponseWire = Schema<'AuthRefreshTokenResponse'>;
export type CurrentUserWire = Schema<'AuthCurrentUserDto'>;
export type UserTenantWire = Schema<'AuthTenantMembershipDto'>;
export type SwitchTenantResponseWire = Schema<'AuthSwitchTenantResponse'>;
export type SessionWire = Schema<'AuthSessionDto'>;
export type MfaEnrollResponseWire = Schema<'AuthMfaEnrollResponse'>;
export type MfaVerifyResponseWire = Schema<'AuthMfaVerifyResponse'>;
export type TenantAuthSettingsWire = Schema<'AuthTenantAuthSettingsResponse'>;
export type TenantUserWire = Schema<'UsersTenantUserListItemDto'>;
export type TenantUserPageWire = Schema<'PagedResultOfUsersTenantUserListItemDto'>;
/** The NON-generic envelope: `{ success, message, code, errors, timestamp }`, no `data` key. */
export type MessageResponseWire = Schema<'ApiResponse'>;

// ─── narrowing helpers (guarded lookups, never a blind `as`) ──────────────────

/**
 * The backend serializes the `TenantStatus` ENUM with `JsonStringEnumConverter`, i.e. PascalCase
 * names — `"Active"`, `"PastDue"`, `"Suspended"` (Program.cs:215-221). The FE union has always been
 * lowercase snake_case. Nothing translated between them, so `tenant.status === 'active'` has never
 * matched a real response. Decoding here is what makes the declared FE semantics reachable at all.
 *
 * Unrecognised / absent → `'suspended'`, never `'active'`: on this field the only consumer is an
 * allow-list switch gate, so an unknown status must block the switch rather than claim a healthy
 * tenant. (Server-side `POST /auth/switch-tenant` remains the real authority — it returns 403.)
 */
const TENANT_STATUS_BY_WIRE: Readonly<Partial<Record<string, TenantStatus>>> = {
  trial: 'trial',
  active: 'active',
  pastdue: 'past_due',
  past_due: 'past_due',
  suspended: 'suspended',
  terminating: 'terminating',
  terminated: 'terminated',
};

function narrowTenantStatus(value: string | null | undefined): TenantStatus {
  return TENANT_STATUS_BY_WIRE[(value ?? '').toLowerCase()] ?? 'suspended';
}

const MFA_POLICIES: readonly ITenantAuthSettings['mfaPolicy'][] = [
  'off',
  'optional',
  'required',
];
const CONCURRENT_SESSION_STRATEGIES: readonly ConcurrentSessionStrategy[] = [
  'deny_new',
  'revoke_oldest',
];
const SSO_ENFORCEMENT_MODES: readonly SsoEnforcementMode[] = ['optional', 'sso_only'];
const SSO_ONBOARDING_STATUSES: readonly SsoOnboardingStatus[] = [
  'not_started',
  'consent_pending',
  'consented',
  'enabled',
];

/**
 * Unrecognised / absent → `'required'`, NEVER `'off'`.
 *
 * `mfaPolicy` is the one settings field that is read-modify-WRITTEN: session-policy, lockout-policy
 * and sso-settings all PUT `{ ...currentSettings, ...formValue }`, and the backend's
 * `TenantAuthSettingsRequest.MfaPolicy` is a NON-nullable string defaulting to `"off"`. So an
 * absent/undefined/null value on the way out silently DISABLES tenant MFA enforcement and wipes
 * `MfaRequiredRoles`. `'off'` and `undefined` are therefore the same, destructive answer; the only
 * non-permissive one left is `'required'`. This fires only on a genuine contract break — the
 * backend's own default is a real `"off"` string that round-trips faithfully.
 */
function narrowMfaPolicy(
  value: string | null | undefined,
): ITenantAuthSettings['mfaPolicy'] {
  return MFA_POLICIES.includes(value as ITenantAuthSettings['mfaPolicy'])
    ? (value as ITenantAuthSettings['mfaPolicy'])
    : 'required';
}

/**
 * The remaining settings unions stay OPTIONAL and resolve to `undefined` when unrecognised. That is
 * deliberate and is NOT a missing default: their backend request counterparts are all nullable
 * ("null = leave unchanged"), and every read site already applies its own restrictive fallback
 * (`concurrentSessionStrategy ?? 'deny_new'`, `enforcementMode ?? 'optional'`,
 * `ssoOnboardingStatus ?? 'not_started'`). Preserving `undefined` keeps those decisions where they
 * are visible instead of freezing an invented value into the next PUT.
 */
function narrowFrom<T extends string>(
  allowed: readonly T[],
  value: string | null | undefined,
): T | undefined {
  return allowed.includes(value as T) ? (value as T) : undefined;
}

// ─── login / refresh / current user ──────────────────────────────────────────

export function mapUser(w: UserWire | undefined): IUser {
  return {
    userId: w?.userId ?? '',
    email: w?.email ?? '',
    displayName: w?.displayName ?? '',
    // NO WIRE SOURCE on the login DTO. `false` = "MFA is not set up" → the UI prompts enrollment.
    // `true` would claim a protection the user may not have. /auth/me is the authoritative read.
    mfaEnabled: false,
  };
}

export function mapTenantInfo(w: TenantInfoWire | undefined): ITenantInfo {
  return {
    tenantId: w?.tenantId ?? '',
    subdomain: w?.subdomain ?? '',
    name: w?.name ?? '',
    // status / logoUrl / primaryColor: no wire source. Left absent ON PURPOSE — see ITenantInfo.
  };
}

export function mapLoginResponse(w: LoginResponseWire): ILoginResponse {
  return {
    // Fails CLOSED: authInterceptor attaches no Authorization header for a falsy token, so an
    // absent token yields an unauthenticated session, never an unauthorized one.
    accessToken: w.accessToken ?? '',
    user: mapUser(w.user),
    tenant: mapTenantInfo(w.tenant),
    // Fails CLOSED: no permissions → hasPermission()/hasAnyPermission() are false everywhere.
    permissions: w.permissions ?? [],
    // `false` cannot bypass MFA: on a real challenge the server withholds the access token, so a
    // wrong `false` lands the user unauthenticated. A wrong `true` would strand EVERY login on an
    // MFA prompt — a total outage — and still grants nothing. Only the server enforces MFA.
    mfaChallenge: w.mfaChallenge ?? false,
    mfaMethod: w.mfaMethod === 'totp' ? 'totp' : undefined,
    // NOT defaulted on purpose: `undefined` is what makes handleLoginResponse fall back to its
    // `!user.mfaEnabled` proxy, which resolves to "enrollment required". Defaulting it to `false`
    // would suppress the enrollment prompt.
    mfaEnrollmentRequired: w.mfaEnrollmentRequired,
    // `undefined` = unknown, NOT 0 = "you have no recovery codes left" (a false alarm).
    recoveryCodesRemaining: w.recoveryCodesRemaining ?? undefined,
    shouldRegenerateRecoveryCodes: w.shouldRegenerateRecoveryCodes,
    // w.refreshToken is deliberately NOT mapped — see the banner.
  };
}

export function mapRefreshResponse(w: RefreshResponseWire): IRefreshResponse {
  // Same fail-closed reasoning as login: '' → no Authorization header → 401 → normal re-login.
  return { accessToken: w.accessToken ?? '' };
}

export function mapCurrentUser(w: CurrentUserWire): ICurrentUserResponse {
  return {
    userId: w.userId ?? '',
    email: w.email ?? '',
    displayName: w.displayName ?? '',
    tenant: mapTenantInfo(w.tenant),
    // FAIL CLOSED. The wire type is `string[] | null`; an absent list must mean "no authority",
    // never "unrestricted". hydrateFromMe keeps its own `?? []` as a second lock.
    roles: w.roles ?? [],
    permissions: w.permissions ?? [],
    // `false` does not claim a protection the account may not have.
    mfaEnabled: w.mfaEnabled ?? false,
    // w.tenantMemberships is real but undeclared here — see the banner.
  };
}

// ─── tenant switcher / switch-tenant ─────────────────────────────────────────

export function mapUserTenant(w: UserTenantWire): IUserTenant {
  return {
    tenantId: w.tenantId ?? '',
    subdomain: w.subdomain ?? '',
    name: w.name ?? '',
    logoUrl: w.logoUrl ?? undefined,
    status: narrowTenantStatus(w.status),
    // `[]` is safe for `primaryRole()` (`roles[0] || 'Member'`) and denies nothing that was granted.
    roles: w.roles ?? [],
    // `false` = "not the tenant you are in". A wrong `true` would mislabel a tenant as current AND
    // disable switching to it; `false` at worst offers a switch the server re-checks.
    isCurrentTenant: w.isCurrentTenant ?? false,
  };
}

export function mapSwitchTenantResponse(
  w: SwitchTenantResponseWire,
): ISwitchTenantResponse {
  return {
    accessToken: w.accessToken ?? '',
    tenant: mapTenantInfo(w.tenant),
    // '' reloads the current page. This value is assigned to `window.location.href`, so any
    // invented fallback would be an open-redirect primitive. Claim nothing.
    redirectUrl: w.redirectUrl ?? '',
    // w.refreshToken deliberately NOT mapped.
  };
}

// ─── sessions ────────────────────────────────────────────────────────────────

export function mapSession(w: SessionWire): ISession {
  return {
    // '' makes the revoke URL 404 — it can never revoke the WRONG session.
    sessionId: w.sessionId ?? '',
    device: w.device ?? '',
    browser: w.browser ?? '',
    os: w.os ?? '',
    ipAddress: w.ipAddress ?? '',
    issuedAt: w.issuedAt ?? '',
    // Nullable on the wire; '' is falsy exactly like the null the cast used to let through.
    lastActiveAt: w.lastActiveAt ?? '',
    // `false` keeps the Revoke button ENABLED. A wrong `true` disables revoke for that row, which
    // is how a hijacked session would become un-killable. Worst case of `false` is self-logout.
    isCurrent: w.isCurrent ?? false,
  };
}

// ─── MFA ─────────────────────────────────────────────────────────────────────

export function mapMfaEnrollResponse(w: MfaEnrollResponseWire): IMfaEnrollResponse {
  return {
    secret: w.secret ?? '',
    qrCodeDataUrl: w.qrCodeDataUrl ?? '',
    // `[]` shows no codes rather than inventing any; a visible failure, not a silent one.
    recoveryCodes: w.recoveryCodes ?? [],
  };
}

export function mapMfaVerifyResponse(w: MfaVerifyResponseWire): IMfaVerifyResponse {
  return {
    // THE fail-closed default on this surface. verifyMfaEnrollment() flips the local user to
    // `mfaEnabled: true` and clears `mfaRequiresEnrollment` on `success`. Defaulting to `true`
    // would mark an account as MFA-protected without the server ever confirming the code.
    success: w.success ?? false,
    recoveryCodes: w.recoveryCodes ?? undefined,
  };
}

// ─── tenant auth settings ────────────────────────────────────────────────────

export function mapTenantAuthSettings(
  w: TenantAuthSettingsWire,
): ITenantAuthSettings {
  return {
    mfaPolicy: narrowMfaPolicy(w.mfaPolicy),
    // Backend request counterpart is non-nullable and defaults to []; [] is the same answer.
    mfaRequiredRoles: w.mfaRequiredRoles ?? [],
    // Everything below is a FAITHFUL PASSTHROUGH of `undefined`. Each read site already applies
    // its own restrictive fallback, and on the write side `undefined` means "leave unchanged"
    // (all of these are nullable on TenantAuthSettingsRequest). Inventing a value here would
    // freeze it into the next read-modify-write PUT.
    idleTimeoutMinutes: w.idleTimeoutMinutes,
    absoluteTimeoutHours: w.absoluteTimeoutHours,
    maxConcurrentSessions: w.maxConcurrentSessions,
    concurrentSessionStrategy: narrowFrom(
      CONCURRENT_SESSION_STRATEGIES,
      w.concurrentSessionStrategy,
    ),
    maxFailedAttempts: w.maxFailedAttempts,
    lockoutDurationMinutes: w.lockoutDurationMinutes,
    progressiveLockoutEnabled: w.progressiveLockoutEnabled,
    ssoEnabled: w.ssoEnabled,
    allowedEntraTenantIds: w.allowedEntraTenantIds ?? undefined,
    allowedEmailDomains: w.allowedEmailDomains ?? undefined,
    jitEnabled: w.jitEnabled,
    jitDefaultRole: w.jitDefaultRole,
    enforcementMode: narrowFrom(SSO_ENFORCEMENT_MODES, w.enforcementMode),
    breakGlassAdminUserIds: w.breakGlassAdminUserIds ?? undefined,
    ssoOnboardingStatus: narrowFrom(SSO_ONBOARDING_STATUSES, w.ssoOnboardingStatus),
    // ssoEntitled is a read-only ENTITLEMENT flag and the SSO card gates on `=== true`.
    // Passed through untouched so an absent flag stays absent and stays fail-closed.
    ssoEntitled: w.ssoEntitled,
  };
}

// ─── admin: tenant users ─────────────────────────────────────────────────────

export function mapTenantUser(w: TenantUserWire): ITenantUser {
  return {
    userId: w.userId ?? '',
    email: w.email ?? '',
    displayName: w.displayName ?? '',
    // SHAPE FIX: the wire sends `TenantUserRoleDto[]` ({roleId, name}) — the FE has always
    // declared and rendered `string[]`. `[]` denies (the break-glass admin filter uses
    // `roles.some(...)`), and templates deref `.length` / `.join()` unguarded.
    roles: (w.roles ?? [])
      .map((r) => r.name ?? '')
      .filter((name) => name.length > 0),
    // Derived from the ENUM-as-PascalCase `status` ("Active" | "Disabled" | "Suspended").
    // Strict `=== 'Active'` fails CLOSED: anything unknown is treated as not-active, which
    // EXCLUDES the user from the sso_only break-glass admin candidate list.
    isActive: w.status === 'Active',
    // NO WIRE SOURCE — no lockout state on this DTO. `null` = "not locked" is the only value the
    // type allows; it cannot be trusted. See the report.
    lockedUntil: null,
    // failedLoginCount / avatarUrl: no wire source, omitted rather than invented.
    lastLoginAt: w.lastLoginAt ?? null,
  };
}

/**
 * `GET /tenant/users` returns `ApiResponse<PagedResult<TenantUserListItemDto>>`. The envelope
 * interceptor unwraps only the OUTER `{ success, data }` — it explicitly leaves a paging envelope
 * alone — so the body that reaches here is the PagedResult, not an array. Both callers
 * (`admin-user-lockout`, `sso-settings`) treat the result as a plain array.
 */
export function mapTenantUserPage(w: TenantUserPageWire | null): ITenantUser[] {
  return (w?.items ?? []).map(mapTenantUser);
}

export function mapMessageResponse(w: MessageResponseWire | null): IMessageResponse {
  // `ApiResponse.Ok(message: null)` is a real backend call, so '' is the honest default.
  return { message: w?.message ?? '' };
}
