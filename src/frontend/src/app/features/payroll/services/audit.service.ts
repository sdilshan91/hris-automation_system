import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { environment } from '../../../../environments/environment';
import {
  AuditEntryWire,
  AuditExportFormat,
  AuditPageWire,
  IAuditEntry,
  IAuditTrailFilters,
  IPage,
  IPayrollHistoryRun,
  IWirePage,
  PayrollHistoryPageWire,
  PayrollHistoryRunWire,
  mapAuditEntry,
  mapAuditPage,
  mapPayrollHistoryPage,
} from '../models/audit.models';

/**
 * US-PAY-012: Payroll History + Audit Trail service.
 *
 * Sibling to PayrollReportService / PayrollRunService; the route strings live here
 * ONLY, so a backend contract change is a one-file fix. The REAL REST surface
 * (base `${apiBaseUrl}/payroll`, where apiBaseUrl already ends in `/api/v1`),
 * reconciled to `PayrollAuditController`:
 *  - GET /payroll/runs/history?year=&month=&status=&sortBy=&descending=&page=&pageSize=
 *        → PayrollRunHistoryPageDto (AC-1, FR-1)
 *  - GET /payroll/audit-trail?fromUtc=&toUtc=&action=&actorUserId=&resourceType=&resourceId=&page=&pageSize=
 *        → PayrollAuditPageDto (AC-4, FR-4)
 *  - GET /payroll/runs/:id/audit-timeline → PayrollAuditEntryDto[] (FR-6, per-run timeline)
 *  - GET /payroll/audit-trail/export?format=csv|xlsx|pdf&<filters> → blob (FR-5)
 *
 * History + audit-trail return a paginated page `{ items, totalCount, page,
 * pageSize }`; the table reads `.items`. The per-run timeline returns a bare
 * array.
 *
 * Tenant isolation (AC-5): every read is tenant-scoped server-side (RLS on
 * audit_log); the FE only sends `withCredentials` + the tenant header (via the
 * tenantInterceptor) and never a tenant id.
 *
 * ENVELOPE: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the JSON methods consume the BARE page payload (but
 * tolerate a `{ data }` wrapper defensively). The export uses `responseType:
 * 'blob'` + `observe: 'response'`, which the envelope interceptor passes through
 * untouched; the caller reads the `Content-Disposition` filename.
 */
@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly payrollUrl = `${environment.apiBaseUrl}/payroll`;
  private readonly historyUrl = `${this.payrollUrl}/runs/history`;
  private readonly auditUrl = `${this.payrollUrl}/audit-trail`;
  private readonly runsUrl = `${this.payrollUrl}/runs`;

  /**
   * The payroll run history page for the History table (AC-1, FR-1). Optional
   * `year` / `status` filters are applied server-side; both omitted = all runs.
   * Returns the BE page; the caller reads `.items`.
   */
  getHistory(
    year: number | null = null,
    status: string | null = null,
  ): Observable<IPage<IPayrollHistoryRun>> {
    let params = new HttpParams();
    if (year !== null) {
      params = params.set('year', `${year}`);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http
      .get<
        | PayrollHistoryPageWire
        | { data: PayrollHistoryPageWire }
        | PayrollHistoryRunWire[]
      >(this.historyUrl, { params, withCredentials: true })
      .pipe(map((res) => mapPayrollHistoryPage(this.toWirePage(res))));
  }

  /**
   * The filtered payroll audit trail page (AC-4, FR-4). Filters by date range,
   * action type, actor, resource type and resource id; any empty filter is
   * omitted so the backend applies no constraint for it. Returns the BE page; the
   * caller reads `.items`.
   */
  getAuditTrail(filters: IAuditTrailFilters): Observable<IPage<IAuditEntry>> {
    return this.http
      .get<AuditPageWire | { data: AuditPageWire } | AuditEntryWire[]>(
        this.auditUrl,
        { params: this.filterParams(filters), withCredentials: true },
      )
      .pipe(map((res) => mapAuditPage(this.toWirePage(res))));
  }

  /**
   * The per-run audit timeline (FR-6) — every action related to one run, in
   * chronological order. Backs the run-detail "Audit Trail" tab. Returns a bare
   * array.
   */
  getRunAuditTrail(runId: string): Observable<IAuditEntry[]> {
    return this.http
      .get<AuditEntryWire[] | { data: AuditEntryWire[] }>(
        `${this.runsUrl}/${runId}/audit-timeline`,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res).map(mapAuditEntry)));
  }

  /**
   * Export the filtered audit trail to a downloadable blob (FR-5). Returns the
   * full response so the caller reads the `Content-Disposition` filename. Bypasses
   * the JSON envelope (responseType: 'blob').
   */
  exportAuditTrail(
    filters: IAuditTrailFilters,
    format: AuditExportFormat,
  ): Observable<HttpResponse<Blob>> {
    const params = this.filterParams(filters).set('format', format);
    return this.http.get(`${this.auditUrl}/export`, {
      params,
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /** Build the shared audit-trail query params, omitting any empty filter. */
  private filterParams(filters: IAuditTrailFilters): HttpParams {
    let params = new HttpParams();
    if (filters.fromUtc) {
      params = params.set('fromUtc', filters.fromUtc);
    }
    if (filters.toUtc) {
      params = params.set('toUtc', filters.toUtc);
    }
    if (filters.action) {
      params = params.set('action', filters.action);
    }
    if (filters.actorUserId) {
      params = params.set('actorUserId', filters.actorUserId);
    }
    if (filters.resourceType) {
      params = params.set('resourceType', filters.resourceType);
    }
    if (filters.resourceId) {
      params = params.set('resourceId', filters.resourceId);
    }
    return params;
  }

  /** Accept either a bare array or a `{ data }` envelope; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }

  /**
   * Normalize a paginated response to a WIRE page: accept the bare page, a
   * `{ data }`-wrapped page, or a bare array (legacy) — and default to an empty
   * page so the table renders an empty state instead of throwing. The item
   * mapper (`mapAuditPage` / `mapPayrollHistoryPage`) runs on the result, so this
   * stays deliberately shape-only.
   */
  private toWirePage<W>(
    res: IWirePage<W> | { data: IWirePage<W> } | W[] | null | undefined,
  ): IWirePage<W> {
    if (Array.isArray(res)) {
      return { items: res, totalCount: res.length, page: 1, pageSize: res.length };
    }
    const page = res && 'data' in res ? res.data : (res as IWirePage<W> | null);
    if (page && Array.isArray(page.items)) {
      return page;
    }
    return { items: [], totalCount: 0, page: 1, pageSize: 0 };
  }
}
