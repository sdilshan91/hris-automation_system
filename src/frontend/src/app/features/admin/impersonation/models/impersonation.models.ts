/**
 * US-ADM-003: System Admin impersonation models.
 *
 * Mirrors the backend contract rooted at `/api/v1/system/impersonation`.
 * The global apiEnvelopeInterceptor strips the `ApiResponse<T>` wrapper, so the
 * service consumes BARE payloads (these interfaces describe the unwrapped body).
 */

/** Request body for POST /system/impersonation (FR-1). */
import type { Schema } from '@core/api';

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

// ─── Wire contract → view-model mappers (D1 admin slice) ─────────────────────
//
// Impersonation is the highest-privilege action in the product: a platform operator acting AS a tenant user.
// `http.post<IStartImpersonationResponse>(…)` asserted the response shape rather than checking it, so a
// renamed field would have arrived as `undefined` on the path that mints an impersonation token — and
// `isReadOnly` defaulting wrongly is the difference between a read-only session and a writable one.
//
// That is why `isReadOnly` defaults to TRUE below. Every other field defaults to an empty value; this one
// defaults to the SAFE value, because an absent flag must not silently grant write access.

export type StartImpersonationWire = Schema<'ImpersonationStartImpersonationResultDto'>;
export type EndImpersonationWire = Schema<'ImpersonationEndImpersonationResultDto'>;
export type ImpersonationTargetWire = Schema<'ImpersonationImpersonationTargetDto'>;

export function mapStartImpersonation(w: StartImpersonationWire): IStartImpersonationResponse {
  return {
    sessionId: w.sessionId ?? '',
    token: w.token ?? '',
    redirectUrl: w.redirectUrl ?? '',
    expiresAt: w.expiresAt ?? '',
    // Fail CLOSED: a missing flag must not be read as "this session may write".
    isReadOnly: w.isReadOnly ?? true,
  };
}

export function mapEndImpersonation(w: EndImpersonationWire): IEndImpersonationResponse {
  return {
    sessionId: w.sessionId ?? '',
    status: (w.status ?? 'ended') as IEndImpersonationResponse['status'],
    actionsCount: w.actionsCount ?? 0,
    endedAt: w.endedAt ?? '',
  };
}

export function mapImpersonationTarget(w: ImpersonationTargetWire): IImpersonationTarget {
  return {
    userId: w.userId ?? '',
    email: w.email ?? '',
    displayName: w.displayName ?? '',
    roles: w.roles ?? [],
  };
}
