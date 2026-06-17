import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
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
 *   GET    /api/v1/users?page=&pageSize=&search=&status=&roleId=
 *   GET    /api/v1/users/:userTenantId            — full detail view
 *   POST   /api/v1/users/invite                   — single or bulk by email
 *   POST   /api/v1/users/invite/csv               — parsed CSV rows
 *   PUT    /api/v1/users/roles                     — replace complete role set
 *   POST   /api/v1/users/deactivate
 *   POST   /api/v1/users/force-password-reset
 *   POST   /api/v1/users/end-sessions
 *   GET    /api/v1/invitations                     — pending invitations
 *   POST   /api/v1/invitations/:id/resend
 *   POST   /api/v1/invitations/:id/revoke
 *   GET    /api/v1/users/assignable-roles          — built-in assignable roles
 */
@Injectable({ providedIn: 'root' })
export class UserManagementService {
  private readonly http = inject(HttpClient);
  private readonly usersUrl = `${environment.apiBaseUrl}/users`;
  private readonly invitationsUrl = `${environment.apiBaseUrl}/invitations`;

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

    return this.http.get<IUserListResponse>(this.usersUrl, {
      params: httpParams,
      withCredentials: true,
    });
  }

  /** Full user detail view (profile, roles, employee, audit, sessions, invites). */
  getUserDetail(userTenantId: string): Observable<IUserDetail> {
    return this.http.get<IUserDetail>(`${this.usersUrl}/${userTenantId}`, {
      withCredentials: true,
    });
  }

  /** Built-in roles assignable to users in this tenant. */
  getAssignableRoles(): Observable<IAssignableRole[]> {
    return this.http.get<IAssignableRole[]>(
      `${this.usersUrl}/assignable-roles`,
      { withCredentials: true }
    );
  }

  // ─── Invitations (AC-2, FR-2, FR-3) ──────────────────────

  /** Invite one or many users by email with a shared role set. */
  inviteUsers(request: IInviteUsersRequest): Observable<IInviteResult[]> {
    return this.http.post<IInviteResult[]>(
      `${this.usersUrl}/invite`,
      request,
      { withCredentials: true }
    );
  }

  /** Bulk invite from parsed CSV rows (email + role names). */
  inviteFromCsv(rows: ICsvInviteRow[]): Observable<IInviteResult[]> {
    const body: IBulkCsvInviteRequest = { rows };
    return this.http.post<IInviteResult[]>(
      `${this.usersUrl}/invite/csv`,
      body,
      { withCredentials: true }
    );
  }

  /** Pending invitations for the current tenant. */
  getInvitations(): Observable<IInvitation[]> {
    return this.http.get<IInvitation[]>(this.invitationsUrl, {
      withCredentials: true,
    });
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

  /** Replace a user's complete role set with the supplied role ids. */
  editRoles(request: IEditRolesRequest): Observable<void> {
    return this.http.put<void>(`${this.usersUrl}/roles`, request, {
      withCredentials: true,
    });
  }

  // ─── Lifecycle actions (AC-4, AC-5, FR-5) ────────────────

  /** Deactivate (disable) a user's membership in this tenant. */
  deactivateUser(userTenantId: string): Observable<void> {
    return this.http.post<void>(
      `${this.usersUrl}/deactivate`,
      { userTenantId },
      { withCredentials: true }
    );
  }

  /** Force a (global) password reset for the user. */
  forcePasswordReset(userTenantId: string): Observable<void> {
    return this.http.post<void>(
      `${this.usersUrl}/force-password-reset`,
      { userTenantId },
      { withCredentials: true }
    );
  }

  /** Revoke all of the user's refresh tokens within this tenant. */
  endAllSessions(userTenantId: string): Observable<void> {
    return this.http.post<void>(
      `${this.usersUrl}/end-sessions`,
      { userTenantId },
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
