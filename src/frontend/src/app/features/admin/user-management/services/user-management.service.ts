import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, forkJoin, map, of } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IUserListParams,
  IUserListResponse,
  IUserDetail,
  IAssignableRole,
  IInviteUsersRequest,
  IBulkCsvInviteRequest,
  ICsvInviteRow,
  IInviteResult,
  IEditRolesRequest,
  IInvitation,
  TenantUserPageWire,
  TenantUserDetailWire,
  InvitationWire,
  InviteResultWire,
  RoleWire,
  mapUserListResponse,
  mapUserDetail,
  mapAssignableRole,
  mapInvitation,
  mapInviteResults,
} from '../models/user-management.models';

/**
 * US-ADM-005: tenant user & role-assignment management service.
 *
 * These are TENANT-scoped endpoints under `/api/v1/users...` (and
 * `/api/v1/invitations...`) — NOT the system-admin `/api/v1/system` root.
 * Tenant isolation is enforced server-side via ITenantContext + EF global
 * filters; here we just carry the auth cookie + X-Tenant-Subdomain header
 * (added by the tenantInterceptor) on every call.
 *
 * Backend contract (finalized in parallel — keep models in one file):
 * GAP-009: this list previously documented routes that DO NOT EXIST, which is largely why the drift went
 * unnoticed — the docstring corroborated the code. Verified against contracts/openapi/hrm-v1.json:
 *
 *   GET    /api/v1/tenant/users?page=&pageSize=&search=&status=&roleId=
 *   GET    /api/v1/tenant/users/:userTenantId/detail          — full detail view
 *   POST   /api/v1/tenant/users/invite                        — ONE { email, roleIds } per call
 *   POST   /api/v1/tenant/users/invite/bulk                   — { rows: [{ email, roleNames }] }
 *   PUT    /api/v1/tenant/users/:userTenantId/roles           — { roleIds }; id in the PATH
 *   POST   /api/v1/tenant/users/:userTenantId/deactivate      — no body
 *   POST   /api/v1/tenant/users/:userTenantId/force-password-reset
 *   POST   /api/v1/tenant/users/:userTenantId/end-sessions
 *   GET    /api/v1/tenant/users/invitations                   — pending invitations
 *   POST   /api/v1/tenant/users/invitations/:id/resend
 *   POST   /api/v1/tenant/users/invitations/:id/revoke
 *   GET    /api/v1/tenant/roles                               — assignable roles (there is no
 *                                                               /users/assignable-roles endpoint)
 */
@Injectable({ providedIn: 'root' })
export class UserManagementService {
  private readonly http = inject(HttpClient);
  private readonly usersUrl = `${environment.apiBaseUrl}/tenant/users`;
  private readonly invitationsUrl = `${environment.apiBaseUrl}/tenant/users/invitations`;

  // ─── User list & detail (AC-1, FR-1, FR-6) ───────────────

  /** Paginated, searchable, filterable user list scoped to the current tenant. */
  getUsers(params: IUserListParams): Observable<IUserListResponse> {
    let httpParams = new HttpParams()
      .set('page', String(params.page))
      .set('pageSize', String(params.pageSize));

    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    if (params.status) {
      httpParams = httpParams.set('status', params.status);
    }
    if (params.roleId) {
      httpParams = httpParams.set('roleId', params.roleId);
    }

    return this.http
      .get<TenantUserPageWire>(this.usersUrl, {
        params: httpParams,
        withCredentials: true,
      })
      .pipe(map(mapUserListResponse));
  }

  /** Full user detail view (profile, roles, employee, audit, sessions, invites). */
  getUserDetail(userTenantId: string): Observable<IUserDetail> {
    // GAP-009: the detail view is /{userTenantId}/detail; the bare /{userTenantId} route does not exist.
    return this.http
      .get<TenantUserDetailWire>(`${this.usersUrl}/${userTenantId}/detail`, {
        withCredentials: true,
      })
      .pipe(map(mapUserDetail));
  }

  /**
   * Roles assignable to users in this tenant.
   *
   * GAP-009: this used to GET `/users/assignable-roles`, which has NEVER existed on the API — zero backend
   * hits. The tenant's roles come from the roles endpoint, whose RoleDto already carries exactly the
   * id/name/description this screen needs.
   */
  getAssignableRoles(): Observable<IAssignableRole[]> {
    return this.http
      .get<RoleWire[]>(`${environment.apiBaseUrl}/tenant/roles`, { withCredentials: true })
      .pipe(map((roles) => (roles ?? []).map(mapAssignableRole)));
  }

  // ─── Invitations (AC-2, FR-2, FR-3) ──────────────────────

  /**
   * Invite one or many users by email with a shared role set.
   *
   * GAP-009: `POST /users/invite` takes a SINGLE `{ email, roleIds }`, not the `{ emails[], roleIds }` this
   * used to send — so a multi-address invite posted a body the API could not bind. Fanned out one request
   * per address and recombined, which preserves this method's `IInviteResult[]` contract and keeps the
   * per-address outcome the UI renders. (`invite/bulk` is not the right endpoint here: it takes role NAMES
   * for the CSV path, while this screen has role IDS.)
   */
  inviteUsers(request: IInviteUsersRequest): Observable<IInviteResult[]> {
    if (request.emails.length === 0) {
      return of([]);
    }

    // Each call answers with ONE `UsersInviteResultDto` `{ created[], errors[] }`;
    // `mapInviteResults` flattens it into the per-address rows the modal renders.
    return forkJoin(
      request.emails.map((email) =>
        this.http.post<InviteResultWire>(
          `${this.usersUrl}/invite`,
          { email, roleIds: request.roleIds },
          { withCredentials: true }
        )
      )
    ).pipe(map((results) => results.flatMap(mapInviteResults)));
  }

  /**
   * Bulk invite from parsed CSV rows (email + role names).
   *
   * D1: this declared `IInviteResult[]` but the endpoint returns a SINGLE
   * `UsersInviteResultDto` object. The modal then called `res.filter(…)` on a
   * non-array, so a successful CSV invite threw a TypeError instead of rendering
   * the per-row outcomes.
   */
  inviteFromCsv(rows: ICsvInviteRow[]): Observable<IInviteResult[]> {
    const body: IBulkCsvInviteRequest = { rows };
    return this.http
      .post<InviteResultWire>(
        // GAP-009: the endpoint is invite/bulk; invite/csv has never existed.
        `${this.usersUrl}/invite/bulk`,
        body,
        { withCredentials: true }
      )
      .pipe(map(mapInviteResults));
  }

  /** Pending invitations for the current tenant. */
  getInvitations(): Observable<IInvitation[]> {
    return this.http
      .get<InvitationWire[]>(this.invitationsUrl, { withCredentials: true })
      .pipe(map((rows) => (rows ?? []).map(mapInvitation)));
  }

  /** Resend an invitation (issues a fresh 72h token). */
  resendInvitation(invitationId: string): Observable<void> {
    return this.http.post<void>(
      `${this.invitationsUrl}/${invitationId}/resend`,
      null,
      { withCredentials: true }
    );
  }

  /** Revoke a pending invitation. */
  revokeInvitation(invitationId: string): Observable<void> {
    return this.http.post<void>(
      `${this.invitationsUrl}/${invitationId}/revoke`,
      null,
      { withCredentials: true }
    );
  }

  // ─── Role assignment (AC-3, FR-4) ────────────────────────

  /**
   * Replace a user's complete role set with the supplied role ids.
   *
   * GAP-009: the membership id belongs in the PATH (`/users/{userTenantId}/roles`); it used to be sent in
   * the body against a `/users/roles` route that does not exist.
   */
  editRoles(request: IEditRolesRequest): Observable<void> {
    return this.http.put<void>(
      `${this.usersUrl}/${request.userTenantId}/roles`,
      { roleIds: request.roleIds },
      { withCredentials: true }
    );
  }

  // ─── Lifecycle actions (AC-4, AC-5, FR-5) ────────────────

  /** Deactivate (disable) a user's membership in this tenant. */
  deactivateUser(userTenantId: string): Observable<void> {
    // GAP-009: the membership id is a PATH segment; the body-carrying /users/deactivate route does not exist.
    return this.http.post<void>(
      `${this.usersUrl}/${userTenantId}/deactivate`,
      null,
      { withCredentials: true }
    );
  }

  /** Force a (global) password reset for the user. */
  forcePasswordReset(userTenantId: string): Observable<void> {
    // GAP-009: the membership id is a PATH segment; the body-carrying /users/force-password-reset route does not exist.
    return this.http.post<void>(
      `${this.usersUrl}/${userTenantId}/force-password-reset`,
      null,
      { withCredentials: true }
    );
  }

  /** Revoke all of the user's refresh tokens within this tenant. */
  endAllSessions(userTenantId: string): Observable<void> {
    // GAP-009: the membership id is a PATH segment; the body-carrying /users/end-sessions route does not exist.
    return this.http.post<void>(
      `${this.usersUrl}/${userTenantId}/end-sessions`,
      null,
      { withCredentials: true }
    );
  }

  // ─── CSV parsing helper (client-side, AC-2 / FR-3) ───────

  /**
   * Parse a raw CSV string into invite rows. Expects a header row containing
   * `email` and `role` columns (role = comma-separated names within a quoted
   * cell, or a single name). Pure + side-effect free so it is unit-testable.
   */
  static parseCsv(raw: string): ICsvInviteRow[] {
    const lines = raw
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => l.length > 0);

    if (lines.length === 0) {
      return [];
    }

    // Detect & skip a header row (first cell looks like "email").
    const firstCells = splitCsvLine(lines[0]);
    const hasHeader = firstCells[0]?.trim().toLowerCase() === 'email';
    const dataLines = hasHeader ? lines.slice(1) : lines;

    const rows: ICsvInviteRow[] = [];
    for (const line of dataLines) {
      const cells = splitCsvLine(line);
      const email = (cells[0] ?? '').trim();
      if (!email) {
        continue;
      }
      const roleNames = (cells[1] ?? '')
        .split(',')
        .map((r) => r.trim())
        .filter((r) => r.length > 0);
      rows.push({ email, roleNames });
    }
    return rows;
  }
}

/**
 * Split a single CSV line into cells, honouring double-quoted cells that may
 * themselves contain commas (used for the comma-separated role list).
 */
function splitCsvLine(line: string): string[] {
  const cells: string[] = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i++;
      } else {
        inQuotes = !inQuotes;
      }
    } else if (ch === ',' && !inQuotes) {
      cells.push(current);
      current = '';
    } else {
      current += ch;
    }
  }
  cells.push(current);
  return cells;
}
