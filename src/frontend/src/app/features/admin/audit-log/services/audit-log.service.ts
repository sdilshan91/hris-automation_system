import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  AuditExportFormat,
  IActorOption,
  IAuditLogDetail,
  IAuditLogFilters,
  IAuditLogListParams,
  IAuditLogPage,
} from '../models/audit-log.models';

/**
 * US-ADM-008: Tenant Admin audit-log viewer service.
 *
 * TENANT-scoped endpoints under `/api/v1/tenant/audit-log...` (NOT the
 * system-admin console — that is `system_audit_log`, BR-3). Base derived from
 * `environment.apiBaseUrl` verbatim (already ends in `/v1`) + `/tenant/audit-log`,
 * matching how US-ADM-007 (`/tenant/workflows`) and US-ADM-006 (`/tenant/settings`)
 * built their bases. Tenant isolation is enforced server-side via ITenantContext
 * + EF global filters; here we just carry the auth cookie + X-Tenant-Subdomain
 * header (added by the tenantInterceptor) and never a tenant id.
 *
 * Backend contract (finalized in parallel — keep models in one file):
 *   GET  /api/v1/tenant/audit-log?page=&pageSize=&startDate=&endDate=
 *          &actorUserId=&action=&resourceType=&search=   → IAuditLogPage  (AC-1/2)
 *   GET  /api/v1/tenant/audit-log/:id                     → IAuditLogDetail (AC-3)
 *   GET  /api/v1/tenant/audit-log/export?format=csv|json&<same filters>
 *          → file download (server logs the export, AC-4/BR-4)
 *
 * The actor autocomplete REUSES the US-ADM-005 user list rather than inventing a
 * new distinct-actors source:
 *   GET  /api/v1/users?pageSize=200&status=active        → user options
 *
 * ENVELOPE: services here consume BARE payloads (no ApiResponse unwrap), matching
 * the rest of the FE. The export uses `responseType: 'blob'` + `observe:
 * 'response'`; the caller reads the `Content-Disposition` filename. A 403 on
 * export (Auditor role, FR-7) propagates so the component can surface a
 * read-only message.
 */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/audit-log`;
  private readonly usersUrl = `${environment.apiBaseUrl}/users`;

  /** Paginated, filterable, reverse-chronological audit-log page (AC-1, AC-2). */
  getAuditLog(params: IAuditLogListParams): Observable<IAuditLogPage> {
    let httpParams = new HttpParams()
      .set('page', String(params.page))
      .set('pageSize', String(params.pageSize));
    httpParams = this.applyFilters(httpParams, params);

    return this.http.get<IAuditLogPage>(this.baseUrl, {
      params: httpParams,
      withCredentials: true,
    });
  }

  /** Full audit record (before/after JSON, user agent, trace id) for AC-3. */
  getAuditDetail(id: string): Observable<IAuditLogDetail> {
    return this.http.get<IAuditLogDetail>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * Export the audit log honouring the current filters (AC-4). Returns the full
   * response so the caller reads the `Content-Disposition` filename. Bypasses the
   * JSON envelope (responseType: 'blob'). An Auditor may receive a 403 (FR-7),
   * which propagates to the component for a read-only message.
   */
  exportAuditLog(
    filters: IAuditLogFilters,
    format: AuditExportFormat
  ): Observable<HttpResponse<Blob>> {
    let params = this.applyFilters(new HttpParams(), filters).set(
      'format',
      format
    );
    return this.http.get(`${this.baseUrl}/export`, {
      params,
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /**
   * Actor options for the actor autocomplete (AC-2). Reuses the US-ADM-005 user
   * list — requests a large page of active users and projects to the minimal
   * option shape the picker needs.
   */
  getActorOptions(): Observable<IActorOption[]> {
    return this.http
      .get<IActorListEnvelope>(this.usersUrl, {
        params: { page: '1', pageSize: '200', status: 'active' },
        withCredentials: true,
      })
      .pipe(
        map((res) =>
          (res.items ?? []).map((u) => ({
            id: u.userTenantId,
            displayName: u.displayName,
            email: u.email,
          }))
        )
      );
  }

  /** Apply the shared filter params, omitting any empty filter (AND logic). */
  private applyFilters(
    params: HttpParams,
    filters: IAuditLogFilters
  ): HttpParams {
    let next = params;
    if (filters.startDate) {
      next = next.set('startDate', filters.startDate);
    }
    if (filters.endDate) {
      next = next.set('endDate', filters.endDate);
    }
    if (filters.actorUserId) {
      next = next.set('actorUserId', filters.actorUserId);
    }
    if (filters.action) {
      next = next.set('action', filters.action);
    }
    if (filters.resourceType) {
      next = next.set('resourceType', filters.resourceType);
    }
    if (filters.search) {
      next = next.set('search', filters.search);
    }
    return next;
  }
}

/** Minimal projection of the US-ADM-005 user-list response we consume here. */
interface IActorListEnvelope {
  items: { userTenantId: string; displayName: string; email: string }[];
}
