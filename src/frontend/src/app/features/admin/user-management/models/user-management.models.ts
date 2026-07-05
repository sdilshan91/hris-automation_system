/**
 * US-ADM-005 — Tenant Admin Manages Users and Role Assignments.
 *
 * All model interfaces live in this single file (per the implementation brief)
 * so the contract can be adjusted in one place while the backend DTOs are
 * finalized in parallel. These are tenant-scoped resources served under
 * `/api/v1/users...` (NOT the system-admin `/api/v1/system` root).
 */

/** Membership status of a user within the current tenant. */
export type UserMembershipStatus = 'invited' | 'active' | 'disabled';

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

/** A pending invitation row (Pending Invitations tab, AC-2). */
export interface IInvitation {
  id: string;
  email: string;
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
