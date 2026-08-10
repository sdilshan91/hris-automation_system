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

/** A user with their current role assignments (for the assignment UI) */
export interface IUserWithRoles {
  userId: string;
  userTenantId: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  roles: IUserRoleAssignment[];
}

/** A role assignment on a user */
export interface IUserRoleAssignment {
  roleId: string;
  roleName: string;
  isBuiltIn: boolean;
  assignedAt: string;
  assignedBy: string;
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
