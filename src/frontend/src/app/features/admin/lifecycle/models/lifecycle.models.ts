/**
 * US-ADM-004: System Admin tenant lifecycle models (suspend / terminate /
 * reactivate / restore).
 *
 * Mirrors the backend contract rooted at `/api/v1/system/tenants/{id}/lifecycle`
 * — UNDER the `/v1/system` namespace (like US-ADM-003 impersonation), NOT the
 * `/api/admin` monitoring namespace. The global apiEnvelopeInterceptor strips the
 * `ApiResponse<T>` wrapper, so the service consumes BARE payloads (these
 * interfaces describe the unwrapped body).
 */

import type { Schema } from '@core/api';

/** Lifecycle status values a tenant can be in. */
export type TenantLifecycleStatus =
  | 'active'
  | 'trial'
  | 'past_due'
  | 'suspended'
  | 'terminating'
  | 'terminated'
  /**
   * A lifecycle status this build does not recognise. Present so the mapper never has to LIE about a
   * tenant's state — see {@link mapTenantLifecycleStatus}.
   */
  | 'unknown';

/** Request body for POST .../lifecycle/suspend (FR-1 / AC-1). */
export interface ISuspendTenantRequest {
  /** Mandatory, 10–500 chars (FR-1 / AC-1). */
  reason: string;
}

/** Request body for POST .../lifecycle/terminate (FR-2 / AC-3). */
export interface ITerminateTenantRequest {
  /** Mandatory, 10–500 chars. */
  reason: string;
  /** Grace period, 7–90 days, default 30 (BR-4 / FR-2). */
  graceDays: number;
}

/**
 * Result returned by suspend / terminate / reactivate / restore
 * (`TenantLifecycleResult`).
 */
export interface ITenantLifecycleResult {
  tenantId: string;
  subdomain: string;
  status: TenantLifecycleStatus;
  /** ISO timestamp — set when suspended, null otherwise. */
  suspendedAt: string | null;
  /** The stored suspension reason, null when not suspended. */
  suspendedReason: string | null;
  /** ISO timestamp — current time + grace period, set when terminating. */
  terminationScheduledAt: string | null;
  /** The lifecycle event type that this transition wrote. */
  eventType: TenantLifecycleEventType;
}

/**
 * Audited lifecycle event types (FR-7).
 *
 * These are NOT a C# enum — the backend writes lowercase snake_case string
 * literals, so the casing matches 1:1. `'plan_changed'` was MISSING: it is
 * written by the plan-change path (ISSUE-341) and appears in real history rows,
 * where it previously fell through every label and dot-colour branch.
 */
export type TenantLifecycleEventType =
  | 'suspended'
  | 'termination_initiated'
  | 'terminated'
  | 'reactivated'
  | 'restored'
  | 'plan_changed';

/**
 * A `tenant_lifecycle_event` row, returned by GET .../lifecycle/history.
 * `detailJson` is the raw audit detail blob (reason, grace days, prior state…).
 */
export interface ITenantLifecycleEvent {
  id: string;
  tenantId: string;
  eventType: TenantLifecycleEventType;
  detailJson: string | null;
  createdAt: string;
  /**
   * Nullable on the wire — system-generated rows (the hard-delete job's
   * `terminated` event) have no acting user. It was declared non-null `string`,
   * so the timeline printed `undefined` after the separator for those rows.
   */
  createdBy: string | null;
}

/** Reason field constraints (FR-1 / AC-1). */
export const LIFECYCLE_REASON_MIN = 10;
export const LIFECYCLE_REASON_MAX = 500;

/** Grace-period bounds (BR-4 / FR-2). */
export const GRACE_DAYS_MIN = 7;
export const GRACE_DAYS_MAX = 90;
export const GRACE_DAYS_DEFAULT = 30;

/** Statuses from which a tenant may be suspended (BR-1). */
export function canSuspend(status: string): boolean {
  return status === 'active' || status === 'past_due' || status === 'trial';
}

/** Statuses from which a tenant may be terminated (BR-1). */
export function canTerminate(status: string): boolean {
  return (
    status === 'active' || status === 'past_due' || status === 'suspended'
  );
}

/** Statuses from which a tenant may be reactivated (BR-1 / AC-5). */
export function canReactivate(status: string): boolean {
  return status === 'suspended';
}

/** Statuses from which a tenant may be restored (BR-1 / BR-3 / AC-6). */
export function canRestore(status: string): boolean {
  return status === 'terminating';
}

/** Tailwind badge classes for a lifecycle status (color-coded). */
export function lifecycleStatusClass(status: string): string {
  switch ((status || '').toLowerCase()) {
    case 'active':
      return 'bg-green-50 text-green-700';
    case 'trial':
      return 'bg-blue-50 text-blue-700';
    case 'past_due':
      return 'bg-orange-50 text-orange-700';
    case 'suspended':
      return 'bg-amber-50 text-amber-700';
    case 'terminating':
      return 'bg-red-50 text-red-700';
    case 'terminated':
      return 'bg-red-100 text-red-800';
    default:
      return 'bg-neutral-100 text-neutral-600';
  }
}

/** Human-readable label for a lifecycle event type (timeline). */
export function lifecycleEventLabel(eventType: TenantLifecycleEventType): string {
  switch (eventType) {
    case 'suspended':
      return 'Suspended';
    case 'termination_initiated':
      return 'Termination initiated';
    case 'terminated':
      return 'Terminated';
    case 'reactivated':
      return 'Reactivated';
    case 'restored':
      return 'Restored';
    default:
      return eventType;
  }
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────
//
// All five lifecycle routes EXIST. The drift is entirely in the STATUS token:
// the wire sends `TenantStatus.ToString()` — PascalCase `Trial | Active | PastDue |
// Suspended | Terminating | Terminated` — while this module's vocabulary is
// lowercase snake_case. Every `canSuspend`/`canTerminate`/`canReactivate`/
// `canRestore` gate compares with `===`, so a raw wire status made all four
// `false` and the quick-action buttons vanished after any transition.
//
// `toLowerCase()` is NOT a fix — `PastDue` becomes `pastdue`, not `past_due`, and
// that is precisely the value the "can I suspend this?" gate cares about. Hence an
// explicit table.

export type TenantLifecycleResultWire = Schema<'TenantsTenantLifecycleResultDto'>;
export type TenantLifecycleEventWire = Schema<'TenantsTenantLifecycleEventDto'>;

/**
 * PascalCase `TenantStatus` → this module's snake_case token. Exported because
 * the monitoring service consumes the same status field and must agree; anything
 * unrecognised maps to `'terminated'`, the most restrictive state, so an unknown
 * value can never enable a destructive action it was not meant to.
 */
export function mapTenantLifecycleStatus(
  wire: string | null | undefined,
): TenantLifecycleStatus {
  switch (wire ?? '') {
    case 'Active':
      return 'active';
    case 'Trial':
      return 'trial';
    case 'PastDue':
      return 'past_due';
    case 'Suspended':
      return 'suspended';
    case 'Terminating':
      return 'terminating';
    default:
      // NEVER default to 'terminated'. That is the most severe state in the union, and an unrecognised
      // value is not evidence of it — the operator would see a red "Terminated" badge for a tenant that is
      // running fine. Every consumer is safe with 'unknown': the four quick-action gates take a `string` and
      // fail closed, and `lifecycleStatusClass` renders a neutral badge for anything it does not know.
      return 'unknown';
  }
}

const LIFECYCLE_EVENT_TYPES: readonly TenantLifecycleEventType[] = [
  'suspended',
  'termination_initiated',
  'terminated',
  'reactivated',
  'restored',
  'plan_changed',
];

/** Narrow with a guard, not a cast; unknown → `'terminated'` (see above). */
export function mapTenantLifecycleEventType(
  wire: string | null | undefined,
): TenantLifecycleEventType {
  const v = (wire ?? '') as TenantLifecycleEventType;
  return LIFECYCLE_EVENT_TYPES.includes(v) ? v : 'terminated';
}

export function mapTenantLifecycleResult(
  w: TenantLifecycleResultWire,
): ITenantLifecycleResult {
  return {
    tenantId: w.tenantId ?? '',
    subdomain: w.subdomain ?? '',
    status: mapTenantLifecycleStatus(w.status),
    suspendedAt: w.suspendedAt ?? null,
    suspendedReason: w.suspendedReason ?? null,
    terminationScheduledAt: w.terminationScheduledAt ?? null,
    eventType: mapTenantLifecycleEventType(w.eventType),
  };
}

export function mapTenantLifecycleEvent(
  w: TenantLifecycleEventWire,
): ITenantLifecycleEvent {
  return {
    id: w.id ?? '',
    tenantId: w.tenantId ?? '',
    eventType: mapTenantLifecycleEventType(w.eventType),
    detailJson: w.detailJson ?? null,
    createdAt: w.createdAt ?? '',
    createdBy: w.createdBy ?? null,
  };
}
