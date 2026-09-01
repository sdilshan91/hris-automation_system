import type { Schema } from '@core/api';

/**
 * GAP-016: was hand-written with `roleId` and a `tenantId` the API does not return, so every navigate and
 * delete hit `/roles/undefined` — and the specs stayed green because they mocked the invented shape. Now
 * derived from the generated contract (`RolesRoleDto` = id/name/description/permissions/isBuiltIn/
 * userCount/createdAt), so a backend rename is a compile error rather than a broken link.
 */
export type IRole = Schema<'RolesRoleDto'> & {
  /**
   * Narrowed from the generated type, which marks EVERY property optional because Swashbuckle emits no
   * `required` for non-nullable C# reference types. The API always sends these two for a role, and the UI
   * cannot render a row without them, so asserting them here keeps call sites readable while the field
   * NAMES — the thing that actually drifted — stay contract-derived. Narrow only what the UI genuinely
   * requires; do not blanket-`Required<>` a generated DTO, which would also strip legitimate nulls.
   */
  id: string;
  name: string;
};

/** Request payload for creating a new custom role */
export interface ICreateRoleRequest {
  name: string;
  description: string;
  permissions: string[];
}

/** Request payload for updating an existing custom role */
export interface IUpdateRoleRequest {
  name: string;
  description: string;
  permissions: string[];
}

/** Request payload for assigning roles to a user */
export interface IAssignRolesRequest {
  roleIds: string[];
}

/**
 * A user with their current role assignments (for the assignment UI).
 *
 * D1 — `avatarUrl` was REMOVED: `UsersTenantUserDetailDto` has no avatar field and
 * never has. Keeping it would mean either an always-`undefined` property or a mapper
 * inventing a URL; both are the drift this migration exists to remove. Nothing
 * rendered it (only the spec fixture set it), so it is gone rather than defaulted.
 */
export interface IUserWithRoles {
  userId: string;
  userTenantId: string;
  email: string;
  displayName: string;
  roles: IUserRoleAssignment[];
}

/**
 * A role assignment on a user.
 *
 * D1 — `isBuiltIn`, `assignedAt` and `assignedBy` were REMOVED. The wire's
 * `UsersTenantUserRoleDto` carries exactly `{ roleId, name }`; the API has no
 * assignment-provenance fields at all. They were only ever set by the spec fixture,
 * so nothing rendered them. See the OUT-OF-LANE note if the audit trail is wanted.
 */
export interface IUserRoleAssignment {
  roleId: string;
  /** RENAMED from the wire's `name` — mapped, not aliased. */
  roleName: string;
}

/** A single permission entry in the catalog */
export interface IPermission {
  key: string;
  label: string;
  description: string;
}

/** A group of permissions under a module */
export interface IPermissionGroup {
  module: string;
  label: string;
  icon: string;
  permissions: IPermission[];
}

// ─── Wire contract → view-model mappers (D1 admin slice 1) ────────────────────

export type RoleWire = Schema<'RolesRoleDto'>;
export type TenantUserDetailWire = Schema<'UsersTenantUserDetailDto'>;
export type TenantUserRoleWire = Schema<'UsersTenantUserRoleDto'>;

/**
 * `IRole` narrows `id`/`name` off the generated DTO because the UI cannot render a
 * row without them. That narrowing used to happen implicitly at the `http.get<IRole>`
 * boundary — i.e. it was asserted, never checked. This mapper makes it explicit: the
 * two narrowed fields get real defaults, every other field stays exactly as the
 * contract declares it.
 */
export function mapRole(w: RoleWire): IRole {
  return { ...w, id: w.id ?? '', name: w.name ?? '' };
}

/** RENAME: the wire calls the role's label `name`; this view-model calls it `roleName`. */
export function mapUserRoleAssignment(
  w: TenantUserRoleWire,
): IUserRoleAssignment {
  return { roleId: w.roleId ?? '', roleName: w.name ?? '' };
}

/**
 * Projects the user-detail DTO onto the role-assignment view-model. The wire DTO also
 * carries `status`, `lastLoginAt`, `linkedEmployeeId`, `activeSessions` and
 * `invitationHistory`; this screen renders none of them.
 */
export function mapUserWithRoles(w: TenantUserDetailWire): IUserWithRoles {
  return {
    userId: w.userId ?? '',
    userTenantId: w.userTenantId ?? '',
    email: w.email ?? '',
    displayName: w.displayName ?? '',
    roles: (w.roles ?? []).map(mapUserRoleAssignment),
  };
}
