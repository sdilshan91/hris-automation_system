/**
 * US-ADM-008 — Tenant Admin Views Audit Logs.
 *
 * All model interfaces + the pure JSON-diff helper live in this single file (per
 * the implementation brief) so the contract can be adjusted in one place while
 * the backend DTOs are finalized in parallel. These are tenant-scoped resources
 * served under `/api/v1/tenant/audit-log...` (NOT the system-admin console).
 *
 * Tenant isolation is enforced server-side via ITenantContext + EF global
 * filters (BR-3); the FE never sends a tenant id — only the auth cookie + the
 * X-Tenant-Subdomain header (added by the tenantInterceptor).
 */

import type { Schema } from '@core/api';

/** Export file formats offered to the user (AC-4 / Data Requirements §7). */
export type AuditExportFormat = 'csv' | 'json';

/** A single row in the paginated audit-log list (AC-1, FR-1, FR-2). */
export interface IAuditLogEntry {
  id: string;
  /** ISO-8601 UTC timestamp of the event. */
  timestamp: string;
  /**
   * Display name of the actor who performed the action. Nullable on the wire —
   * system / unauthenticated events have no actor — and the list template calls
   * `initials(entry.actorName)`, so the mapper substitutes `''`, never `undefined`.
   */
  actorName: string;
  /** Email of the actor (shown under the name). Nullable on the wire. */
  actorEmail: string;
  /** Dotted action key, e.g. "Employee.Create", "Leave.Approve". */
  action: string;
  /** Resource type acted upon, e.g. "Employee", "LeaveRequest". Nullable on the wire. */
  resourceType: string;
  /**
   * The actor's user id — on the wire all along, previously dropped by this model.
   * OPTIONAL, matching the contract: `AuditLogAuditLogListItemDto` marks it
   * optional and a system-generated event genuinely has no actor. Nothing renders
   * it today; it is captured so the actor filter has a contract-derived source
   * instead of an invented one.
   */
  actorUserId?: string | null;
  /** Id of the affected resource (may be null for non-entity events). */
  resourceId: string | null;
  /** Originating IP address. */
  ipAddress: string | null;
  /** Short human-readable summary of the change. */
  summary: string;
}

/**
 * The full audit record returned by the detail endpoint (AC-3, FR-2, FR-3).
 * `before` / `after` arrive as JSON strings, already sensitive-masked
 * server-side as "***REDACTED***" (FR-4) — the FE renders them verbatim.
 */
export interface IAuditLogDetail extends Omit<IAuditLogEntry, 'summary'> {
  /**
   * ⚠ NO WIRE SOURCE. `AuditLogDetailDto` has no `summary` field at all — only
   * the LIST DTO carries one. This used to be inherited as a required `string`
   * via `extends IAuditLogEntry`, so the detail panel seeded `summary` from the
   * clicked row and then overwrote the whole object with the detail response,
   * silently wiping it and hiding the Summary block. Made optional so the caller
   * has to decide (merge the row in, or render the empty state) rather than
   * being handed an `undefined` typed as `string`.
   */
  summary?: string;
  userAgent: string | null;
  traceId: string | null;
  /**
   * The actor's employee number. OPTIONAL for the same reason as `actorUserId`:
   * the wire marks it optional, and an actor with no employee record has none.
   */
  actorEmployeeNo?: string | null;
  /** JSON string of the pre-change state (or null for create/auth events). */
  before: string | null;
  /** JSON string of the post-change state (or null for delete events). */
  after: string | null;
}

/** Pagination + metadata wrapper returned by the list endpoint (FR-1, BR-5). */
export interface IAuditLogPage {
  items: IAuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  /** Plan-governed retention window, surfaced as a read-only badge (BR-5). */
  retentionDays: number;
}

/** Query filters for the audit-log list (AC-2, FR-1). Empty fields are omitted. */
export interface IAuditLogFilters {
  /** ISO date (yyyy-MM-dd) lower bound, inclusive. */
  startDate?: string;
  /** ISO date (yyyy-MM-dd) upper bound, inclusive. */
  endDate?: string;
  /** Actor user id to filter by. */
  actorUserId?: string;
  /**
   * Exact action key (single-select, kept for backward compatibility — the
   * export dialog & older callers still rely on it). Prefer `actions` for the
   * multi-select filter (US-NTF-005 / FR-2).
   */
  action?: string;
  /**
   * Exact resource type (single-select, kept for backward compatibility).
   * Prefer `resourceTypes` for the multi-select filter (US-NTF-005 / FR-2).
   */
  resourceType?: string;
  /**
   * Multi-select action keys (US-NTF-005 / FR-2). Serialized as repeated
   * `actions=a&actions=b` query params alongside (or instead of) `action`.
   */
  actions?: string[];
  /**
   * Multi-select resource types (US-NTF-005 / FR-2). Serialized as repeated
   * `resourceTypes=r&resourceTypes=s` query params.
   */
  resourceTypes?: string[];
  /** Free-text keyword search over the change summary / JSON (FR-1). */
  search?: string;
}

/** Combined filter + pagination params for a list request. */
export interface IAuditLogListParams extends IAuditLogFilters {
  page: number;
  pageSize: number;
}

/** An actor option for the actor autocomplete (reuses the US-ADM-005 users). */
export interface IActorOption {
  id: string;
  displayName: string;
  email: string;
}

/**
 * A single actor match from the dedicated actor search endpoint
 * (US-NTF-005 / FR-2): `GET /tenant/audit-log/actors?search=`. The backend
 * returns `{ userId, name, email }`; the component maps this to the uniform
 * `IActorOption` shape used by the picker.
 */
export interface IActorSearchResult {
  userId: string;
  /** Nullable on the wire; the mapper substitutes `''` for the picker label. */
  name: string;
  email: string;
}

/**
 * Distinct filter option lists for the multi-select Action / Resource Type
 * dropdowns (US-NTF-005 / FR-2): `GET /tenant/audit-log/filter-options`.
 * Empty arrays degrade gracefully — the component falls back to options
 * derived from the loaded rows.
 */
export interface IFilterOptions {
  actions: string[];
  resourceTypes: string[];
}

// ─── JSON diff helper (AC-3 / FR-3) ──────────────────────────

/** The kind of change a single field underwent between before and after. */
export type DiffChangeType = 'added' | 'removed' | 'modified' | 'unchanged';

/** A single field-level diff row rendered in the detail panel. */
export interface IDiffEntry {
  key: string;
  /** Stringified value in the `before` state (null when added). */
  oldVal: string | null;
  /** Stringified value in the `after` state (null when removed). */
  newVal: string | null;
  changeType: DiffChangeType;
}

/**
 * Parse a JSON string into a flat key→value record for diffing. Returns an empty
 * record for null/blank/invalid input so the diff degrades gracefully rather than
 * throwing. Nested objects/arrays are stringified so they compare structurally.
 */
export function parseAuditJson(raw: string | null | undefined): Record<string, unknown> {
  if (!raw || !raw.trim()) {
    return {};
  }
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
    // A bare array / scalar — wrap it under a synthetic key so it still diffs.
    return { value: parsed };
  } catch {
    return {};
  }
}

/** Stringify a value for stable display + comparison (objects → JSON). */
function stringifyValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (typeof value === 'object') {
    return JSON.stringify(value);
  }
  return String(value);
}

/**
 * Pure field-level diff of two already-parsed JSON objects (AC-3 / FR-3).
 *
 * Sensitive values arrive pre-masked as "***REDACTED***" from the server (FR-4);
 * this helper treats them as ordinary strings and renders them as-is — it never
 * unmasks. Keys are returned in stable (sorted) order so the panel is
 * deterministic.
 *
 * - present only in `after`  → `added`
 * - present only in `before` → `removed`
 * - present in both, differing values → `modified`
 * - present in both, equal values     → `unchanged`
 */
export function diffAuditJson(
  before: Record<string, unknown>,
  after: Record<string, unknown>
): IDiffEntry[] {
  const keys = Array.from(
    new Set([...Object.keys(before), ...Object.keys(after)])
  ).sort();

  return keys.map((key) => {
    const hasBefore = Object.prototype.hasOwnProperty.call(before, key);
    const hasAfter = Object.prototype.hasOwnProperty.call(after, key);
    const oldStr = hasBefore ? stringifyValue(before[key]) : null;
    const newStr = hasAfter ? stringifyValue(after[key]) : null;

    let changeType: DiffChangeType;
    if (hasBefore && !hasAfter) {
      changeType = 'removed';
    } else if (!hasBefore && hasAfter) {
      changeType = 'added';
    } else if (oldStr !== newStr) {
      changeType = 'modified';
    } else {
      changeType = 'unchanged';
    }

    return { key, oldVal: oldStr, newVal: newStr, changeType };
  });
}

/** Convenience: diff two raw JSON strings end-to-end (parse → diff). */
export function diffAuditRaw(
  beforeRaw: string | null | undefined,
  afterRaw: string | null | undefined
): IDiffEntry[] {
  return diffAuditJson(parseAuditJson(beforeRaw), parseAuditJson(afterRaw));
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────
//
// All five audit-log routes EXIST — the drift here is purely field-level:
// `actorName` / `actorEmail` / `resourceType` are nullable on the wire but were
// declared non-null; `actorUserId` and `actorEmployeeNo` are sent and were dropped;
// and the detail DTO has no `summary` at all (see `IAuditLogDetail`).

export type AuditLogListItemWire = Schema<'AuditLogAuditLogListItemDto'>;
export type AuditLogPageWire = Schema<'AuditLogAuditLogPageDto'>;
export type AuditLogDetailWire = Schema<'AuditLogAuditLogDetailDto'>;
export type AuditLogActorWire = Schema<'AuditLogAuditLogActorDto'>;
export type AuditLogFilterOptionsWire = Schema<'AuditLogAuditLogFilterOptionsDto'>;
export type TenantUserPageWire = Schema<'PagedResultOfUsersTenantUserListItemDto'>;

export function mapAuditLogEntry(w: AuditLogListItemWire): IAuditLogEntry {
  return {
    id: w.id ?? '',
    timestamp: w.timestamp ?? '',
    actorName: w.actorName ?? '',
    actorEmail: w.actorEmail ?? '',
    actorUserId: w.actorUserId ?? null,
    action: w.action ?? '',
    resourceType: w.resourceType ?? '',
    resourceId: w.resourceId ?? null,
    ipAddress: w.ipAddress ?? null,
    summary: w.summary ?? '',
  };
}

export function mapAuditLogPage(w: AuditLogPageWire): IAuditLogPage {
  return {
    items: (w.items ?? []).map(mapAuditLogEntry),
    totalCount: w.totalCount ?? 0,
    page: w.page ?? 1,
    pageSize: w.pageSize ?? 0,
    retentionDays: w.retentionDays ?? 0,
  };
}

/** `summary` is deliberately absent — the detail DTO does not carry one. */
export function mapAuditLogDetail(w: AuditLogDetailWire): IAuditLogDetail {
  return {
    id: w.id ?? '',
    timestamp: w.timestamp ?? '',
    actorName: w.actorName ?? '',
    actorEmail: w.actorEmail ?? '',
    actorUserId: w.actorUserId ?? null,
    actorEmployeeNo: w.actorEmployeeNo ?? null,
    action: w.action ?? '',
    resourceType: w.resourceType ?? '',
    resourceId: w.resourceId ?? null,
    ipAddress: w.ipAddress ?? null,
    userAgent: w.userAgent ?? null,
    traceId: w.traceId ?? null,
    before: w.before ?? null,
    after: w.after ?? null,
  };
}

/** RENAMES: `id` ← `userId`, `displayName` ← `name`. */
export function mapActorOption(w: AuditLogActorWire): IActorOption {
  return {
    id: w.userId ?? '',
    displayName: w.name ?? '',
    email: w.email ?? '',
  };
}

export function mapFilterOptions(
  w: AuditLogFilterOptionsWire,
): IFilterOptions {
  return {
    actions: w.actions ?? [],
    resourceTypes: w.resourceTypes ?? [],
  };
}

/** RENAME: the option's `id` is the membership's `userTenantId`. */
export function mapUserActorOptions(w: TenantUserPageWire): IActorOption[] {
  return (w.items ?? []).map((u) => ({
    id: u.userTenantId ?? '',
    displayName: u.displayName ?? '',
    email: u.email ?? '',
  }));
}
