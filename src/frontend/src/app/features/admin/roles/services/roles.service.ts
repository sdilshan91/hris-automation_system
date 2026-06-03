import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IRole,
  ICreateRoleRequest,
  IUpdateRoleRequest,
  IAssignRolesRequest,
  IUserWithRoles,
} from '../models/role.models';

/**
 * Service for managing tenant roles and user role assignments.
 * Codes to the backend API contracts defined in US-AUTH-006 FR-6:
 *   GET/POST /api/v1/tenant/roles
 *   GET/PUT/DELETE /api/v1/tenant/roles/{id}
 *   PATCH /api/v1/tenant/users/{id} with { roleIds[] }
 */
@Injectable({ providedIn: 'root' })
export class RolesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/roles`;
  private readonly usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  // ─── Role CRUD ───────────────────────────────────────────

  /** Get all roles for the current tenant (built-in + custom) */
  getRoles(): Observable<IRole[]> {
    return this.http.get<IRole[]>(this.baseUrl, { withCredentials: true });
  }

  /** Get a single role by ID */
  getRole(roleId: string): Observable<IRole> {
    return this.http.get<IRole>(`${this.baseUrl}/${roleId}`, {
      withCredentials: true,
    });
  }

  /** Create a new custom role */
  createRole(request: ICreateRoleRequest): Observable<IRole> {
    return this.http.post<IRole>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing custom role */
  updateRole(roleId: string, request: IUpdateRoleRequest): Observable<IRole> {
    return this.http.put<IRole>(`${this.baseUrl}/${roleId}`, request, {
      withCredentials: true,
    });
  }

  /** Delete a custom role */
  deleteRole(roleId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${roleId}`, {
      withCredentials: true,
    });
  }

  // ─── User Role Assignment ────────────────────────────────

  /** Get a user with their current role assignments */
  getUserWithRoles(userTenantId: string): Observable<IUserWithRoles> {
    return this.http.get<IUserWithRoles>(
      `${this.usersUrl}/${userTenantId}`,
      { withCredentials: true }
    );
  }

  /** Assign roles to a user (replaces current assignments) */
  assignRoles(
    userTenantId: string,
    request: IAssignRolesRequest
  ): Observable<IUserWithRoles> {
    return this.http.patch<IUserWithRoles>(
      `${this.usersUrl}/${userTenantId}`,
      request,
      { withCredentials: true }
    );
  }
}
