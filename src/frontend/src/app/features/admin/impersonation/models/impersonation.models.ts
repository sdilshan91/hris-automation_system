/**
 * US-ADM-003: System Admin impersonation models.
 *
 * Mirrors the backend contract rooted at `/api/v1/system/impersonation`.
 * The global apiEnvelopeInterceptor strips the `ApiResponse<T>` wrapper, so the
 * service consumes BARE payloads (these interfaces describe the unwrapped body).
 */

/** Request body for POST /system/impersonation (FR-1). */
export interface IStartImpersonationRequest {
  targetUserId: string;
  targetTenantId: string;
  /** Mandatory, min 10 / max 500 chars (BR-4 / AC-1). */
  reason: string;
}

/** 201 response from POST /system/impersonation. */
export interface IStartImpersonationResponse {
  sessionId: string;
  /** The impersonation JWT (a separate token type, not a refreshed user token). */
  token: string;
  /** Tenant subdomain URL the impersonated session targets (production redirect). */
  redirectUrl: string;
  /** ISO timestamp — session hard-expiry (NFR-2: max 60 min). */
  expiresAt: string;
  /** AC-5/AC-6/BR-1: suspended-tenant or SystemSupport sessions are read-only. */
  isReadOnly: boolean;
}

/** 200 response from POST /system/impersonation/{sessionId}/end (AC-3). */
export interface IEndImpersonationResponse {
  sessionId: string;
  status: 'ended' | 'expired' | 'active';
  actionsCount: number;
  endedAt: string;
}

/**
 * A candidate impersonation target — an active member of the tenant.
 * System users are already excluded by the backend (BR-2).
 */
export interface IImpersonationTarget {
  userId: string;
  email: string;
  displayName: string;
  roles: string[];
}

/** Reason field constraints (BR-4 / AC-1). */
export const IMPERSONATION_REASON_MIN = 10;
export const IMPERSONATION_REASON_MAX = 500;
