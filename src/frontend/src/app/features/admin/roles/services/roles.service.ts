import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IRole,
  ICreateRoleRequest,
  IUpdateRoleRequest,
  IAssignRolesRequest,
  IUserWithRoles,
  RoleWire,
  TenantUserDetailWire,
  mapRole,
  mapUserWithRoles,
} from '../models/role.models';

/**
 * Service for managing tenant roles and user role assignments.
 * Codes to the backend API contracts defined in US-AUTH-006 FR-6:
 *   GET/POST /api/v1/tenant/roles
 *   GET/PUT/DELETE /api/v1/tenant/roles/{id}
 *   PATCH /api/v1/tenant/users/{id} with { roleIds[] }
 *
 * ⚠ REPORTED, NOT FIXED HERE (D1 is a types-only pass):
 *   • `getUserWithRoles` GETs `/tenant/users/{userTenantId}`, which DOES NOT EXIST in
 *     contracts/openapi/hrm-v1.json — the user-detail route is
 *     `/tenant/users/{userTenantId}/detail`. Only the PATCH on the bare path exists.
 *     The URL is left alone; the response TYPE is migrated to the DTO the real route
 *     returns (`UsersTenantUserDetailDto`), which is what this method always meant.
 *   • `PATCH /tenant/users/{userTenantId}` returns a bare `ApiResponse` with NO
 *     `data`, so `assignRoles` now returns `Observable<void>` (see its docstring).
 */
@Injectable({ providedIn: 'root' })
export class RolesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/roles`;
  private readonly usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  // ─── Role CRUD ───────────────────────────────────────────

  /** Get all roles for the current tenant (built-in + custom) */
  getRoles(): Observable<IRole[]> {
    return this.http
      .get<RoleWire[]>(this.baseUrl, { withCredentials: true })
      .pipe(map((roles) => (roles ?? []).map(mapRole)));
  }

  /** Get a single role by ID */
  getRole(roleId: string): Observable<IRole> {
    return this.http
      .get<RoleWire>(`${this.baseUrl}/${roleId}`, { withCredentials: true })
      .pipe(map(mapRole));
  }

  /** Create a new custom role */
  createRole(request: ICreateRoleRequest): Observable<IRole> {
    return this.http
      .post<RoleWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapRole));
  }

  /** Update an existing custom role */
  updateRole(roleId: string, request: IUpdateRoleRequest): Observable<IRole> {
    return this.http
      .put<RoleWire>(`${this.baseUrl}/${roleId}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapRole));
  }

  /** Delete a custom role */
  deleteRole(roleId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${roleId}`, {
      withCredentials: true,
    });
  }

  // ─── User Role Assignment ────────────────────────────────

  /**
   * Get a user with their current role assignments.
   *
   * ⚠ Live 404 — see the class docstring. The type is migrated; the URL is not.
   */
  getUserWithRoles(userTenantId: string): Observable<IUserWithRoles> {
    return this.http
      .get<TenantUserDetailWire>(`${this.usersUrl}/${userTenantId}`, {
        withCredentials: true,
      })
      .pipe(map(mapUserWithRoles));
  }

  /**
   * Assign roles to a user (replaces current assignments).
   *
   * Returns `void`: `PATCH /tenant/users/{id}` responds with a bare `ApiResponse`
   * carrying no `data`. The old `Observable<IUserWithRoles>` was an unchecked cast —
   * and the caller dereferenced it (`updatedUser.roles.map(…)`), so a successful save
   * threw a TypeError on a value that was always `undefined`.
   */
  assignRoles(
    userTenantId: string,
    request: IAssignRolesRequest
  ): Observable<void> {
    return this.http.patch<void>(
      `${this.usersUrl}/${userTenantId}`,
      request,
      { withCredentials: true }
    );
  }
}
