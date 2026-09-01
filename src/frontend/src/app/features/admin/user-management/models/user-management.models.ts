/**
 * US-ADM-005 — Tenant Admin Manages Users and Role Assignments.
 *
 * All model interfaces live in this single file (per the implementation brief)
 * so the contract can be adjusted in one place while the backend DTOs are
 * finalized in parallel. These are tenant-scoped resources served under
 * `/api/v1/users...` (NOT the system-admin `/api/v1/system` root).
 */

import type { Schema } from '@core/api';

/**
 * Membership status of a user within the current tenant — the FE's NORMALIZED
 * vocabulary. The wire sends `UserTenantStatus.ToString()`, i.e. PascalCase
 * `Active | Disabled | Suspended`; `mapMembershipStatus` below is the translation.
 *
 * Two honest mismatches, reported rather than papered over:
 *  • `'suspended'` was MISSING here even though the backend enum has had it all
 *    along — a suspended membership fell through every badge/filter branch. Added.
 *  • `'invited'` has NO backend counterpart: a pending invite is a separate
 *    `UserInvitation` row, never a `UserTenant.Status`. No mapper can ever produce
 *    it. It is retained only because the list's status <select> offers it as a
 *    filter value (which the API cannot match) — see the OUT-OF-LANE note.
 */
export type UserMembershipStatus =
  | 'invited'
  | 'active'
  | 'disabled'
  | 'suspended';

/** A role reference as embedded in user summaries. */
export interface IUserRoleRef {
  id: string;
  name: string;
}

/** A single row in the paginated user list (AC-1, FR-1). */
export interface IUserSummary {
  userTenantId: string;
  userId: string;
  displayName: string;
  email: string;
  roles: IUserRoleRef[];
  status: UserMembershipStatus;
  lastLoginAt: string | null;
  /** Linked employee record id, if this membership is tied to an employee. */
  employeeId?: string | null;
}

/**
 * Pagination metadata wrapper returned by the user list endpoint. Mirrors the
 * backend canonical `PagedResult<T>` shape exactly — { items, page, pageSize,
 * totalCount, totalPages }. NOTE: the total row count is `totalCount` (NOT
 * `total`); reading the wrong name yields `undefined` → `NaN` in the footer
 * (BUG-103, same FE↔BE shape-drift class as BUG-099).
 */
export interface IUserListResponse {
  items: IUserSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Query parameters for the user list (paginated/searchable/filterable). */
export interface IUserListParams {
  page: number;
  pageSize: number;
  search?: string;
  status?: UserMembershipStatus | '';
  roleId?: string;
}

/** An assignable built-in role (from the roles endpoint). */
export interface IAssignableRole {
  id: string;
  name: string;
  description: string;
}

/** Body for a single-or-bulk invite by email + role ids (AC-2, FR-2). */
export interface IInviteUsersRequest {
  emails: string[];
  roleIds: string[];
}

/** A parsed CSV row for bulk invite (AC-2, FR-3). Roles given by name. */
export interface ICsvInviteRow {
  email: string;
  roleNames: string[];
}

/** Body for the bulk CSV invite endpoint — parsed rows. */
export interface IBulkCsvInviteRequest {
  rows: ICsvInviteRow[];
}

/** Per-email / per-row invite result. */
export interface IInviteResult {
  email: string;
  status: 'invited' | 'error';
  error?: string;
}

/** Body for replacing a user's complete role set (AC-3, FR-4). */
export interface IEditRolesRequest {
  userTenantId: string;
  roleIds: string[];
}

/** Body for membership-targeted actions (deactivate / end-sessions). */
export interface IUserTenantAction {
  userTenantId: string;
}

/** Body for force password reset (global — keyed by userTenantId, AC-5). */
export interface IForcePasswordResetRequest {
  userTenantId: string;
}

/**
 * A pending invitation row (Pending Invitations tab, AC-2).
 *
 * `status` is the FE's normalized vocabulary; the wire sends
 * `InvitationStatus.ToString()` = PascalCase `Invited | Accepted | Expired | Revoked`.
 * Note the wire's `Invited` maps to this type's `'pending'` — a RENAME, done in
 * `mapInvitationStatus`, not by redefining the union.
 */
export interface IInvitation {
  id: string;
  email: string;
  /**
   * ⚠ Role NAMES have no wire source. `UsersInvitationDto` carries only
   * `invitedRoleIds: Guid[]`, so `mapInvitation` fills `name` with `''` rather than
   * inventing a label. The invitations table renders these as chips, so they render
   * blank — see the OUT-OF-LANE note.
   */
  roles: IUserRoleRef[];
  invitedAt: string;
  expiresAt: string;
  status: 'pending' | 'expired' | 'accepted' | 'revoked';
}

/** A recent audit entry shown in the user detail view (FR-6). */
export interface IUserAuditEntry {
  id: string;
  action: string;
  actor: string;
  at: string;
  detail?: string;
}

/** An active session shown in the user detail view (FR-6). */
export interface IUserSession {
  id: string;
  device: string;
  ipAddress: string;
  lastActiveAt: string;
}

/** A linked employee summary in the user detail view (FR-6). */
export interface ILinkedEmployee {
  employeeId: string;
  fullName: string;
  jobTitle?: string | null;
  department?: string | null;
}

/**
 * Full user detail view payload (FR-6). Sections are optional so the UI can
 * render gracefully when the backend omits one (e.g. no linked employee).
 */
export interface IUserDetail {
  userTenantId: string;
  userId: string;
  displayName: string;
  email: string;
  status: UserMembershipStatus;
  lastLoginAt: string | null;
  roles: IUserRoleRef[];
  linkedEmployee?: ILinkedEmployee | null;
  recentAudit?: IUserAuditEntry[];
  activeSessions?: IUserSession[];
  invitationHistory?: IInvitation[];
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────
//
// Every read below used to be an unchecked `http.get<I…>` cast. What that hid:
//   • role refs are `{ roleId, name }` on the wire, `{ id, name }` here (RENAME);
//   • the employee link is `linkedEmployeeId`, not `employeeId` (RENAME);
//   • statuses are PascalCase C# enum names, not the lowercase FE unions (RENAME);
//   • `POST /invite` and `POST /invite/bulk` both return ONE `UsersInviteResultDto`
//     `{ created[], errors[] }` — never the per-email `IInviteResult[]` this service
//     declared. `inviteFromCsv` typed a single object as an array, so the component's
//     `res.filter(…)` was calling an array method on a non-array.
// Three fields have NO wire source at all and are reported, not defaulted to
// plausible values: `IUserDetail.recentAudit`, `IUserDetail.linkedEmployee`, and the
// role NAMES on `IInvitation.roles`.

export type TenantUserRoleWire = Schema<'UsersTenantUserRoleDto'>;
export type TenantUserListItemWire = Schema<'UsersTenantUserListItemDto'>;
export type TenantUserPageWire = Schema<'PagedResultOfUsersTenantUserListItemDto'>;
export type TenantUserDetailWire = Schema<'UsersTenantUserDetailDto'>;
export type ActiveSessionWire = Schema<'UsersActiveSessionDto'>;
export type InvitationWire = Schema<'UsersInvitationDto'>;
export type InviteResultWire = Schema<'UsersInviteResultDto'>;
export type RoleWire = Schema<'RolesRoleDto'>;

/**
 * PascalCase `UserTenantStatus` → the FE union. An explicit table, not
 * `toLowerCase()`: the components' existing `toLowerCase()` workarounds (ISSUE-211)
 * only worked because these three names happen to be single words, and they leave
 * an unrecognised value silently styled as neutral. Anything unknown lands on
 * `'disabled'` here — the conservative reading, since the only way a membership is
 * NOT usable is if it is not Active.
 */
export function mapMembershipStatus(
  wire: string | null | undefined,
): UserMembershipStatus {
  switch ((wire ?? '').toLowerCase()) {
    case 'active':
      return 'active';
    case 'suspended':
      return 'suspended';
    default:
      return 'disabled';
  }
}

/** PascalCase `InvitationStatus` → the FE union. `Invited` is called `pending` here. */
export function mapInvitationStatus(
  wire: string | null | undefined,
): IInvitation['status'] {
  switch ((wire ?? '').toLowerCase()) {
    case 'invited':
      return 'pending';
    case 'accepted':
      return 'accepted';
    case 'revoked':
      return 'revoked';
    default:
      return 'expired';
  }
}

/** RENAME: the wire keys a role ref by `roleId`; this view-model keys it by `id`. */
export function mapUserRoleRef(w: TenantUserRoleWire): IUserRoleRef {
  return { id: w.roleId ?? '', name: w.name ?? '' };
}

/** RENAME: `employeeId` ← the wire's `linkedEmployeeId`. */
export function mapUserSummary(w: TenantUserListItemWire): IUserSummary {
  return {
    userTenantId: w.userTenantId ?? '',
    userId: w.userId ?? '',
    displayName: w.displayName ?? '',
    email: w.email ?? '',
    roles: (w.roles ?? []).map(mapUserRoleRef),
    status: mapMembershipStatus(w.status),
    lastLoginAt: w.lastLoginAt ?? null,
    employeeId: w.linkedEmployeeId ?? null,
  };
}

/**
 * `PagedResult<T>` field names match 1:1 — the total row count is `totalCount`, not
 * `total` (BUG-103). Consuming the generated type is what now guarantees that.
 */
export function mapUserListResponse(w: TenantUserPageWire): IUserListResponse {
  return {
    items: (w.items ?? []).map(mapUserSummary),
    page: w.page ?? 1,
    pageSize: w.pageSize ?? 0,
    totalCount: w.totalCount ?? 0,
    totalPages: w.totalPages ?? 0,
  };
}

/** RENAME: `device` ← the wire's `userAgent` (the wire has no separate device field). */
export function mapUserSession(w: ActiveSessionWire): IUserSession {
  return {
    id: w.id ?? '',
    device: w.userAgent ?? '',
    ipAddress: w.ipAddress ?? '',
    lastActiveAt: w.lastActiveAt ?? '',
  };
}

/**
 * ⚠ `roles[].name` is `''` for every entry: `UsersInvitationDto` sends
 * `invitedRoleIds` and nothing else about the roles. Filling the name would mean
 * inventing it, so it is left blank and reported.
 */
export function mapInvitation(w: InvitationWire): IInvitation {
  return {
    id: w.id ?? '',
    email: w.email ?? '',
    roles: (w.invitedRoleIds ?? []).map((id) => ({ id, name: '' })),
    invitedAt: w.invitedAt ?? '',
    expiresAt: w.expiresAt ?? '',
    status: mapInvitationStatus(w.status),
  };
}

/**
 * ⚠ Two sections of this view-model have NO wire source and are therefore absent,
 * not fabricated:
 *   • `recentAudit` — `UsersTenantUserDetailDto` has no audit field whatsoever.
 *   • `linkedEmployee` — the wire carries only `linkedEmployeeId`; the employee's
 *     name / job title / department would need a second call to the employees API.
 * Both are optional on `IUserDetail`, so the detail screen renders its documented
 * empty states instead of showing invented rows.
 */
export function mapUserDetail(w: TenantUserDetailWire): IUserDetail {
  return {
    userTenantId: w.userTenantId ?? '',
    userId: w.userId ?? '',
    displayName: w.displayName ?? '',
    email: w.email ?? '',
    status: mapMembershipStatus(w.status),
    lastLoginAt: w.lastLoginAt ?? null,
    roles: (w.roles ?? []).map(mapUserRoleRef),
    linkedEmployee: null,
    activeSessions: (w.activeSessions ?? []).map(mapUserSession),
    invitationHistory: (w.invitationHistory ?? []).map(mapInvitation),
  };
}

/** The roles endpoint's `RolesRoleDto` projected onto the assignable-role option. */
export function mapAssignableRole(w: RoleWire): IAssignableRole {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    description: w.description ?? '',
  };
}

/**
 * Flattens ONE `UsersInviteResultDto` into the per-address rows the invite modal
 * renders. Both `/invite` and `/invite/bulk` return this same `{ created, errors }`
 * shape; the per-address `IInviteResult` the UI wants is derived from it, never
 * received from the API.
 */
export function mapInviteResults(w: InviteResultWire): IInviteResult[] {
  return [
    ...(w.created ?? []).map(
      (c): IInviteResult => ({ email: c.email ?? '', status: 'invited' }),
    ),
    ...(w.errors ?? []).map(
      (e): IInviteResult => ({
        email: e.email ?? '',
        status: 'error',
        error: e.error ?? undefined,
      }),
    ),
  ];
}
