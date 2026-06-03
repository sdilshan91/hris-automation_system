/** A role within a tenant */
export interface IRole {
  roleId: string;
  tenantId: string | null;
  name: string;
  description: string;
  isBuiltIn: boolean;
  permissions: string[];
  userCount: number;
  createdAt: string;
}

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
